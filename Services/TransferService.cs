using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartBank.Data;
using SmartBank.DTOs.Common;
using SmartBank.Entities;
using SmartBank.Services.Interfaces;

namespace SmartBank.Services
{
    public class TransferService : ITransferService
    {
        private readonly SmartBankDbContext _context;
        private readonly IOtpService _otpService;
        private readonly IEmailService _emailService;
        private readonly ILogger<TransferService> _logger;

        public TransferService(
            SmartBankDbContext context,
            IOtpService otpService,
            IEmailService emailService,
            ILogger<TransferService> logger)
        {
            _context = context;
            _otpService = otpService;
            _emailService = emailService;
            _logger = logger;
        }

        private string MaskEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
                return "***@***.com";

            var parts = email.Split('@');
            var name = parts[0];
            var domain = parts[1];

            if (name.Length <= 2)
            {
                return $"{name[0]}***@{domain}";
            }
            return $"{name[0]}***{name[^1]}@{domain}";
        }

        private string MaskAccountNumber(string accountNumber)
        {
            if (string.IsNullOrEmpty(accountNumber) || accountNumber.Length < 4)
                return "****";
            return $"****{accountNumber.Substring(accountNumber.Length - 4)}";
        }

        public async Task<TransferRequest?> GetPendingTransferAsync(int userId, int transferRequestId)
        {
            return await _context.TransferRequests
                .Include(t => t.SourceAccount)
                .Include(t => t.DestinationAccount)
                .FirstOrDefaultAsync(t => t.Id == transferRequestId && t.UserId == userId);
        }

        public async Task<(int StatusCode, ApiResponse<InitiateTransferResult> Response)> InitiateTransferAsync(
            int userId, string recipientAccountNumber, decimal amount, string? memo)
        {
            if (amount <= 0)
            {
                return (400, ApiResponse<InitiateTransferResult>.FailureResponse("Transfer amount must be greater than ৳0."));
            }

            if (string.IsNullOrWhiteSpace(recipientAccountNumber))
            {
                return (400, ApiResponse<InitiateTransferResult>.FailureResponse("Recipient account number is required."));
            }

            // 1. Fetch user & verify status & verified email
            var senderUser = await _context.Users
                .Include(u => u.Accounts)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (senderUser == null)
            {
                return (404, ApiResponse<InitiateTransferResult>.FailureResponse("Sender user account not found."));
            }

            var now = DateTime.UtcNow;
            if (senderUser.Status != null && senderUser.Status.Equals("Suspended", StringComparison.OrdinalIgnoreCase))
            {
                return (403, ApiResponse<InitiateTransferResult>.FailureResponse("Your account is placed under administrative suspension. Transfers are prohibited."));
            }

            if (!senderUser.IsEmailVerified)
            {
                return (403, ApiResponse<InitiateTransferResult>.FailureResponse(
                    "Email verification is mandatory before initiating money transfers. Please verify your email address first."));
            }

            var senderAccount = senderUser.Accounts.FirstOrDefault();
            if (senderAccount == null || !senderAccount.IsActive)
            {
                return (403, ApiResponse<InitiateTransferResult>.FailureResponse("Your banking account is unavailable or frozen."));
            }

            // 2. Validate recipient account
            var cleanRecAcc = recipientAccountNumber.Trim();
            var recipientUser = await _context.Users
                .Include(u => u.Accounts)
                .FirstOrDefaultAsync(u => u.Accounts.Any(a => a.AccountNumber == cleanRecAcc));

            if (recipientUser == null)
            {
                return (404, ApiResponse<InitiateTransferResult>.FailureResponse($"Recipient account '{cleanRecAcc}' was not found in the SmartBank network."));
            }

            var recipientAccount = recipientUser.Accounts.FirstOrDefault(a => a.AccountNumber == cleanRecAcc);
            if (recipientAccount == null)
            {
                return (404, ApiResponse<InitiateTransferResult>.FailureResponse("Recipient account not found."));
            }

            if (recipientAccount.Id == senderAccount.Id)
            {
                return (400, ApiResponse<InitiateTransferResult>.FailureResponse("Self-transfers are not permitted. Please specify a different recipient account."));
            }

            if (recipientUser.Status != null && recipientUser.Status.Equals("Suspended", StringComparison.OrdinalIgnoreCase))
            {
                return (403, ApiResponse<InitiateTransferResult>.FailureResponse("Transfer declined: The recipient account is under administrative suspension."));
            }

            if (!recipientAccount.IsActive)
            {
                return (403, ApiResponse<InitiateTransferResult>.FailureResponse("Transfer declined: The recipient account is frozen and cannot receive incoming funds."));
            }

            if (amount > senderAccount.Balance)
            {
                return (400, ApiResponse<InitiateTransferResult>.FailureResponse($"Insufficient balance. You currently have ৳{senderAccount.Balance:N2} available."));
            }

            // 3. Create Pending TransferRequest (NO MONEY MOVED YET)
            var transferRequest = new TransferRequest
            {
                UserId = senderUser.Id,
                SourceAccountId = senderAccount.Id,
                DestinationAccountId = recipientAccount.Id,
                Amount = amount,
                Memo = memo?.Trim(),
                Status = TransferRequestStatus.PendingOtp,
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(5),
                CompletedAt = null
            };

            _context.TransferRequests.Add(transferRequest);
            await _context.SaveChangesAsync();

            // 4. Generate OTP challenge bound to this TransferRequestId
            var (challenge, plainOtp) = await _otpService.CreateChallengeAsync(
                senderUser.Id,
                "Transfer",
                transferRequest.Id.ToString(),
                expiryMinutes: 5);

            transferRequest.OtpChallengeId = challenge.Id;
            await _context.SaveChangesAsync();

            // 5. Dispatch OTP Email
            await _emailService.SendTransferOtpEmailAsync(senderUser.Email, recipientAccount.AccountNumber, amount, plainOtp);

            var result = new InitiateTransferResult
            {
                TransferRequestId = transferRequest.Id,
                Amount = amount,
                RecipientAccountNumber = recipientAccount.AccountNumber,
                MaskedRecipient = MaskAccountNumber(recipientAccount.AccountNumber),
                MaskedEmail = MaskEmail(senderUser.Email),
                ExpiresAt = transferRequest.ExpiresAt
            };

            return (200, ApiResponse<InitiateTransferResult>.SuccessResponse(
                result,
                $"A 6-digit verification code has been dispatched to {result.MaskedEmail}. Please verify within 5 minutes to complete the transfer."));
        }

        public async Task<(int StatusCode, ApiResponse<CompleteTransferResult> Response)> VerifyAndCompleteTransferAsync(
            int userId, int transferRequestId, string otp)
        {
            if (string.IsNullOrWhiteSpace(otp))
            {
                return (400, ApiResponse<CompleteTransferResult>.FailureResponse("Please provide the 6-digit verification OTP."));
            }

            using var dbTransaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. Fetch TransferRequest
                var transferRequest = await _context.TransferRequests
                    .Include(t => t.SourceAccount)
                    .Include(t => t.DestinationAccount)
                    .FirstOrDefaultAsync(t => t.Id == transferRequestId);

                if (transferRequest == null || transferRequest.UserId != userId)
                {
                    return (404, ApiResponse<CompleteTransferResult>.FailureResponse("Transfer request not found or does not belong to you."));
                }

                // 2. Prevent replay attacks & verify pending state
                if (transferRequest.Status == TransferRequestStatus.Completed)
                {
                    return (409, ApiResponse<CompleteTransferResult>.FailureResponse("This transfer has already been completed. Double processing prevented."));
                }

                if (transferRequest.Status != TransferRequestStatus.PendingOtp)
                {
                    return (400, ApiResponse<CompleteTransferResult>.FailureResponse($"This transfer cannot be completed because its status is {transferRequest.Status}."));
                }

                if (DateTime.UtcNow > transferRequest.ExpiresAt)
                {
                    transferRequest.Status = TransferRequestStatus.Expired;
                    await _context.SaveChangesAsync();
                    await dbTransaction.CommitAsync();
                    return (400, ApiResponse<CompleteTransferResult>.FailureResponse("This transfer request has expired. Please initiate a new transfer."));
                }

                // 3. Verify OTP Challenge
                var (isValidOtp, otpError) = await _otpService.ValidateChallengeAsync(
                    userId,
                    "Transfer",
                    transferRequest.Id.ToString(),
                    otp);

                if (!isValidOtp)
                {
                    return (400, ApiResponse<CompleteTransferResult>.FailureResponse(otpError ?? "Invalid verification code."));
                }

                // 4. Re-check accounts and balance at time of execution
                var senderAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == transferRequest.SourceAccountId);
                var recipientAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == transferRequest.DestinationAccountId);

                if (senderAccount == null || !senderAccount.IsActive)
                {
                    return (400, ApiResponse<CompleteTransferResult>.FailureResponse("Sender account is unavailable or frozen."));
                }

                if (recipientAccount == null || !recipientAccount.IsActive)
                {
                    return (400, ApiResponse<CompleteTransferResult>.FailureResponse("Recipient account is unavailable or frozen."));
                }

                if (transferRequest.Amount > senderAccount.Balance)
                {
                    return (400, ApiResponse<CompleteTransferResult>.FailureResponse($"Insufficient balance! Available balance is ৳{senderAccount.Balance:N2}."));
                }

                // 5. ATOMIC MONEY MOVEMENT
                var now = DateTime.UtcNow;
                senderAccount.Balance -= transferRequest.Amount;
                senderAccount.UpdatedAt = now;

                recipientAccount.Balance += transferRequest.Amount;
                recipientAccount.UpdatedAt = now;

                var outTx = new Transaction
                {
                    AccountId = senderAccount.Id,
                    Type = TransactionType.TransferOut,
                    Amount = transferRequest.Amount,
                    Timestamp = now,
                    RelatedAccountId = recipientAccount.Id
                };

                var inTx = new Transaction
                {
                    AccountId = recipientAccount.Id,
                    Type = TransactionType.TransferIn,
                    Amount = transferRequest.Amount,
                    Timestamp = now,
                    RelatedAccountId = senderAccount.Id
                };

                _context.Transactions.Add(outTx);
                _context.Transactions.Add(inTx);

                // 6. Mark TransferRequest as Completed
                transferRequest.Status = TransferRequestStatus.Completed;
                transferRequest.CompletedAt = now;

                await _context.SaveChangesAsync();
                await dbTransaction.CommitAsync();

                // 7. Send Confirmation Email
                var senderUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
                if (senderUser != null)
                {
                    await _emailService.SendTransferConfirmationEmailAsync(
                        senderUser.Email,
                        recipientAccount.AccountNumber,
                        transferRequest.Amount,
                        $"TXN-{outTx.Id:D8}");
                }

                var result = new CompleteTransferResult
                {
                    TransferRequestId = transferRequest.Id,
                    Amount = transferRequest.Amount,
                    SenderNewBalance = senderAccount.Balance,
                    RecipientAccountNumber = recipientAccount.AccountNumber,
                    TransactionId = $"TXN-{outTx.Id:D8}",
                    CompletedAt = now
                };

                _logger.LogInformation("Transfer {RequestId} of ৳{Amount} completed successfully from {SenderAcc} to {RecAcc}",
                    transferRequest.Id, transferRequest.Amount, senderAccount.AccountNumber, recipientAccount.AccountNumber);

                return (200, ApiResponse<CompleteTransferResult>.SuccessResponse(
                    result,
                    $"Successfully transferred ৳{transferRequest.Amount:N2} to account {recipientAccount.AccountNumber}. New Balance: ৳{senderAccount.Balance:N2}"));
            }
            catch (Exception ex)
            {
                await dbTransaction.RollbackAsync();
                _logger.LogError(ex, "Error processing transfer {RequestId}", transferRequestId);
                return (500, ApiResponse<CompleteTransferResult>.FailureResponse("An unexpected error occurred while executing the transfer."));
            }
        }

        public async Task<(int StatusCode, ApiResponse<InitiateTransferResult> Response)> ResendTransferOtpAsync(int userId, int transferRequestId)
        {
            var transferRequest = await _context.TransferRequests
                .Include(t => t.DestinationAccount)
                .FirstOrDefaultAsync(t => t.Id == transferRequestId && t.UserId == userId);

            if (transferRequest == null)
            {
                return (404, ApiResponse<InitiateTransferResult>.FailureResponse("Transfer request not found."));
            }

            if (transferRequest.Status != TransferRequestStatus.PendingOtp)
            {
                return (400, ApiResponse<InitiateTransferResult>.FailureResponse($"Cannot resend code for transfer with status '{transferRequest.Status}'."));
            }

            var (success, errorMsg, newPlainOtp) = await _otpService.ResendChallengeAsync(
                userId,
                "Transfer",
                transferRequestId.ToString(),
                cooldownSeconds: 60);

            if (!success || newPlainOtp == null)
            {
                return (429, ApiResponse<InitiateTransferResult>.FailureResponse(errorMsg ?? "Unable to resend verification code at this time."));
            }

            // Refresh expiration
            transferRequest.ExpiresAt = DateTime.UtcNow.AddMinutes(5);
            await _context.SaveChangesAsync();

            var senderUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (senderUser != null)
            {
                await _emailService.SendTransferOtpEmailAsync(
                    senderUser.Email,
                    transferRequest.DestinationAccount.AccountNumber,
                    transferRequest.Amount,
                    newPlainOtp);
            }

            var result = new InitiateTransferResult
            {
                TransferRequestId = transferRequest.Id,
                Amount = transferRequest.Amount,
                RecipientAccountNumber = transferRequest.DestinationAccount.AccountNumber,
                MaskedRecipient = MaskAccountNumber(transferRequest.DestinationAccount.AccountNumber),
                MaskedEmail = MaskEmail(senderUser?.Email ?? ""),
                ExpiresAt = transferRequest.ExpiresAt
            };

            return (200, ApiResponse<InitiateTransferResult>.SuccessResponse(result, "A new verification code has been dispatched to your email."));
        }
    }
}
