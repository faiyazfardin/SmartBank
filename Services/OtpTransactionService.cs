using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartBank.Data;
using SmartBank.DTOs.Common;
using SmartBank.DTOs.Transactions;
using SmartBank.Entities;
using SmartBank.Services.Interfaces;

namespace SmartBank.Services
{
    public class OtpTransactionService : IOtpTransactionService
    {
        private readonly SmartBankDbContext _context;
        private readonly IEmailService _emailService;
        private readonly ILogger<OtpTransactionService> _logger;

        public OtpTransactionService(
            SmartBankDbContext context,
            IEmailService emailService,
            ILogger<OtpTransactionService> logger)
        {
            _context = context;
            _emailService = emailService;
            _logger = logger;
        }

        private static string Generate6DigitOtp()
        {
            var bytes = new byte[4];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            var number = Math.Abs(BitConverter.ToInt32(bytes, 0)) % 1000000;
            return number.ToString("D6");
        }

        private static string MaskEmail(string email)
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

        private static string MaskAccountNumber(string accountNumber)
        {
            if (string.IsNullOrEmpty(accountNumber) || accountNumber.Length < 4)
                return "****";
            return $"****{accountNumber.Substring(accountNumber.Length - 4)}";
        }

        public async Task<(int StatusCode, ApiResponse<OtpChallengeResult> Response)> InitiateTransactionAsync(int userId, TransactionRequestDto request)
        {
            if (request.Amount <= 0)
            {
                return (400, ApiResponse<OtpChallengeResult>.FailureResponse("Transaction amount must be greater than ৳0."));
            }

            var type = request.TransactionType?.Trim();
            if (string.IsNullOrWhiteSpace(type) ||
                (type != "Deposit" && type != "Withdraw" && type != "Transfer" && type != "BillPayment"))
            {
                return (400, ApiResponse<OtpChallengeResult>.FailureResponse("Invalid transaction type. Must be Deposit, Withdraw, Transfer, or BillPayment."));
            }

            // 1. Fetch & validate user
            var user = await _context.Users
                .Include(u => u.Accounts)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                return (404, ApiResponse<OtpChallengeResult>.FailureResponse("User account not found."));
            }

            var now = DateTime.UtcNow;
            if (user.Status != null && user.Status.Equals("Suspended", StringComparison.OrdinalIgnoreCase))
            {
                return (403, ApiResponse<OtpChallengeResult>.FailureResponse("Transaction declined: Your account is placed under administrative suspension."));
            }

            if (!user.IsEmailVerified)
            {
                return (403, ApiResponse<OtpChallengeResult>.FailureResponse("Email verification is mandatory before executing transactions. Please verify your email address."));
            }

            var account = user.Accounts.FirstOrDefault();
            if (account == null || !account.IsActive)
            {
                return (403, ApiResponse<OtpChallengeResult>.FailureResponse("Transaction declined: Your banking account is unavailable or frozen."));
            }

            Account? recipientAccount = null;
            string? targetInfo = null;
            string? reference = request.Reference?.Trim();

            // Additional validation per transaction type
            if (type == "Withdraw" || type == "BillPayment" || type == "Transfer")
            {
                if (request.Amount > account.Balance)
                {
                    return (400, ApiResponse<OtpChallengeResult>.FailureResponse($"Insufficient balance. You currently have ৳{account.Balance:N2} available."));
                }
            }

            if (type == "Transfer")
            {
                var cleanRecAcc = request.RecipientAccount?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(cleanRecAcc))
                {
                    return (400, ApiResponse<OtpChallengeResult>.FailureResponse("Recipient account number is required for transfers."));
                }

                recipientAccount = await _context.Accounts
                    .Include(a => a.User)
                    .FirstOrDefaultAsync(a => a.AccountNumber == cleanRecAcc);

                if (recipientAccount == null)
                {
                    return (404, ApiResponse<OtpChallengeResult>.FailureResponse($"Recipient account '{cleanRecAcc}' was not found."));
                }

                if (recipientAccount.Id == account.Id)
                {
                    return (400, ApiResponse<OtpChallengeResult>.FailureResponse("Self-transfers are not permitted."));
                }

                if (!recipientAccount.IsActive || (recipientAccount.User != null && recipientAccount.User.Status != null && recipientAccount.User.Status.Equals("Suspended", StringComparison.OrdinalIgnoreCase)))
                {
                    return (403, ApiResponse<OtpChallengeResult>.FailureResponse("Transfer declined: The recipient account is unavailable or frozen."));
                }

                targetInfo = $"Recipient Account: {MaskAccountNumber(recipientAccount.AccountNumber)}";
            }
            else if (type == "BillPayment")
            {
                var biller = request.BillerName?.Trim();
                if (string.IsNullOrWhiteSpace(biller))
                {
                    return (400, ApiResponse<OtpChallengeResult>.FailureResponse("Biller name is required for bill payments."));
                }
                targetInfo = $"Biller: {biller}";
            }
            else if (type == "Withdraw")
            {
                targetInfo = "ATM Cash Withdrawal";
            }
            else if (type == "Deposit")
            {
                targetInfo = "Account Deposit Credit";
            }

            // 2. Create PendingTransaction (NO MONEY MOVED YET)
            var pendingTransaction = new PendingTransaction
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                AccountId = account.Id,
                TransactionType = type,
                Amount = request.Amount,
                RecipientAccount = recipientAccount?.AccountNumber,
                BillerName = request.BillerName?.Trim(),
                Reference = reference,
                Status = "PendingOtp",
                CreatedAt = now,
                CompletedAt = null,
                TrackingId = null
            };

            _context.PendingTransactions.Add(pendingTransaction);
            await _context.SaveChangesAsync();

            // 3. Generate 6-digit OTP & BCrypt Hash
            var plainOtp = Generate6DigitOtp();
            var hashedOtp = BCrypt.Net.BCrypt.HashPassword(plainOtp);

            // Rule 2: IssuedAt + 120s validity
            var expiresAt = now.AddSeconds(120);

            var challenge = new OtpChallenge
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                HashedOtp = hashedOtp,
                TransactionType = type,
                TransactionId = pendingTransaction.Id,
                TransactionAmount = request.Amount,
                TargetInfo = targetInfo,
                IssuedAt = now,
                ExpiresAt = expiresAt,
                LastSentAt = now,
                IsUsed = false,
                UsedAt = null,
                AttemptCount = 0,
                MaxAttempts = 5,
                Status = "Pending"
            };

            _context.OtpChallenges.Add(challenge);
            await _context.SaveChangesAsync();

            // 4. Send Email OTP via EmailService
            await _emailService.SendUniversalTransactionOtpAsync(
                user.Email,
                user.FullName,
                type,
                request.Amount,
                targetInfo,
                reference,
                plainOtp);

            var result = new OtpChallengeResult
            {
                ChallengeId = challenge.Id,
                TransactionId = pendingTransaction.Id,
                TransactionType = type,
                Amount = request.Amount,
                TargetInfo = targetInfo,
                MaskedEmail = MaskEmail(user.Email),
                IssuedAt = now,
                ExpiresAt = expiresAt,
                CooldownRemainingSeconds = 60
            };

            return (200, ApiResponse<OtpChallengeResult>.SuccessResponse(
                result,
                $"A 6-digit verification OTP has been dispatched to {result.MaskedEmail}. Valid for 2 minutes."));
        }

        public async Task<(int StatusCode, ApiResponse<OtpVerificationResult> Response)> VerifyOtpAndCommitAsync(Guid challengeId, string userEnteredOtp)
        {
            if (string.IsNullOrWhiteSpace(userEnteredOtp) || userEnteredOtp.Trim().Length != 6)
            {
                return (400, ApiResponse<OtpVerificationResult>.FailureResponse("Please enter a valid 6-digit OTP code."));
            }

            var cleanOtp = userEnteredOtp.Trim();
            var now = DateTime.UtcNow;

            using var dbTransaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. Fetch Challenge & PendingTransaction
                var challenge = await _context.OtpChallenges
                    .Include(c => c.User)
                    .FirstOrDefaultAsync(c => c.Id == challengeId);

                if (challenge == null)
                {
                    return (404, ApiResponse<OtpVerificationResult>.FailureResponse("OTP challenge request not found."));
                }

                var pendingTx = await _context.PendingTransactions
                    .FirstOrDefaultAsync(p => p.Id == challenge.TransactionId);

                if (pendingTx == null)
                {
                    return (404, ApiResponse<OtpVerificationResult>.FailureResponse("Associated pending transaction not found."));
                }

                // Rule 4: Strict Transaction Isolation
                if (challenge.TransactionId != pendingTx.Id)
                {
                    return (400, ApiResponse<OtpVerificationResult>.FailureResponse("Security violation: OTP does not match this transaction."));
                }

                // Rule 3: Single-Use Check
                if (challenge.IsUsed || challenge.Status == "Used")
                {
                    return (400, ApiResponse<OtpVerificationResult>.FailureResponse("OTP has already been used."));
                }

                // Rule 6: Attempt Limit Check
                if (challenge.AttemptCount >= challenge.MaxAttempts || challenge.Status == "Locked")
                {
                    challenge.Status = "Locked";
                    await _context.SaveChangesAsync();
                    await dbTransaction.CommitAsync();
                    return (400, ApiResponse<OtpVerificationResult>.FailureResponse("Too many invalid attempts. This OTP has been locked. Please request a new OTP."));
                }

                // Rule 2: Expiry Check (120 seconds)
                if (now > challenge.ExpiresAt || challenge.Status == "Expired")
                {
                    challenge.Status = "Expired";
                    pendingTx.Status = "Expired";
                    await _context.SaveChangesAsync();
                    await dbTransaction.CommitAsync();
                    return (400, ApiResponse<OtpVerificationResult>.FailureResponse("OTP expired. Please request a new one."));
                }

                // 2. Verify BCrypt OTP Hash
                bool isMatch = false;
                try
                {
                    isMatch = BCrypt.Net.BCrypt.Verify(cleanOtp, challenge.HashedOtp);
                }
                catch (Exception)
                {
                    isMatch = false;
                }

                if (!isMatch)
                {
                    challenge.AttemptCount++;
                    if (challenge.AttemptCount >= challenge.MaxAttempts)
                    {
                        challenge.Status = "Locked";
                    }
                    await _context.SaveChangesAsync();
                    await dbTransaction.CommitAsync();

                    if (challenge.Status == "Locked")
                    {
                        return (400, ApiResponse<OtpVerificationResult>.FailureResponse("Too many invalid attempts. This OTP is locked. Please initiate a new transaction."));
                    }

                    int remainingAttempts = challenge.MaxAttempts - challenge.AttemptCount;
                    return (400, ApiResponse<OtpVerificationResult>.FailureResponse($"Invalid OTP code. {remainingAttempts} attempt(s) remaining."));
                }

                // 3. OTP VERIFIED SUCCESSFUL -> Mark Used immediately
                challenge.IsUsed = true;
                challenge.UsedAt = now;
                challenge.Status = "Used";

                // 4. ATOMIC TRANSACTION COMMIT BASED ON TRANSACTION TYPE
                var userAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == pendingTx.AccountId);
                if (userAccount == null || !userAccount.IsActive)
                {
                    return (400, ApiResponse<OtpVerificationResult>.FailureResponse("Banking account is unavailable or frozen."));
                }

                var trackingId = $"TXN-{Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper()}";

                if (pendingTx.TransactionType == "Deposit")
                {
                    userAccount.Balance += pendingTx.Amount;
                    userAccount.UpdatedAt = now;

                    var txLog = new Transaction
                    {
                        AccountId = userAccount.Id,
                        Type = TransactionType.Deposit,
                        Amount = pendingTx.Amount,
                        Timestamp = now
                    };
                    _context.Transactions.Add(txLog);
                }
                else if (pendingTx.TransactionType == "Withdraw")
                {
                    if (pendingTx.Amount > userAccount.Balance)
                    {
                        return (400, ApiResponse<OtpVerificationResult>.FailureResponse($"Insufficient balance! Available: ৳{userAccount.Balance:N2}"));
                    }

                    userAccount.Balance -= pendingTx.Amount;
                    userAccount.UpdatedAt = now;

                    var txLog = new Transaction
                    {
                        AccountId = userAccount.Id,
                        Type = TransactionType.Withdraw,
                        Amount = pendingTx.Amount,
                        Timestamp = now
                    };
                    _context.Transactions.Add(txLog);
                }
                else if (pendingTx.TransactionType == "BillPayment")
                {
                    if (pendingTx.Amount > userAccount.Balance)
                    {
                        return (400, ApiResponse<OtpVerificationResult>.FailureResponse($"Insufficient balance! Available: ৳{userAccount.Balance:N2}"));
                    }

                    userAccount.Balance -= pendingTx.Amount;
                    userAccount.UpdatedAt = now;

                    var txLog = new Transaction
                    {
                        AccountId = userAccount.Id,
                        Type = TransactionType.Withdraw,
                        Amount = pendingTx.Amount,
                        Timestamp = now
                    };
                    _context.Transactions.Add(txLog);
                }
                else if (pendingTx.TransactionType == "Transfer")
                {
                    if (pendingTx.Amount > userAccount.Balance)
                    {
                        return (400, ApiResponse<OtpVerificationResult>.FailureResponse($"Insufficient balance! Available: ৳{userAccount.Balance:N2}"));
                    }

                    var recipientAccount = await _context.Accounts
                        .FirstOrDefaultAsync(a => a.AccountNumber == pendingTx.RecipientAccount);

                    if (recipientAccount == null || !recipientAccount.IsActive)
                    {
                        return (400, ApiResponse<OtpVerificationResult>.FailureResponse("Recipient account is unavailable or frozen."));
                    }

                    userAccount.Balance -= pendingTx.Amount;
                    userAccount.UpdatedAt = now;

                    recipientAccount.Balance += pendingTx.Amount;
                    recipientAccount.UpdatedAt = now;

                    var outTx = new Transaction
                    {
                        AccountId = userAccount.Id,
                        Type = TransactionType.TransferOut,
                        Amount = pendingTx.Amount,
                        Timestamp = now,
                        RelatedAccountId = recipientAccount.Id
                    };

                    var inTx = new Transaction
                    {
                        AccountId = recipientAccount.Id,
                        Type = TransactionType.TransferIn,
                        Amount = pendingTx.Amount,
                        Timestamp = now,
                        RelatedAccountId = userAccount.Id
                    };

                    _context.Transactions.Add(outTx);
                    _context.Transactions.Add(inTx);
                }

                // Update PendingTransaction state
                pendingTx.Status = "Completed";
                pendingTx.CompletedAt = now;
                pendingTx.TrackingId = trackingId;

                await _context.SaveChangesAsync();
                await dbTransaction.CommitAsync();

                // 5. Send Confirmation Email
                if (challenge.User != null)
                {
                    await _emailService.SendUniversalTransactionConfirmationAsync(
                        challenge.User.Email,
                        challenge.User.FullName,
                        pendingTx.TransactionType,
                        pendingTx.Amount,
                        trackingId,
                        userAccount.Balance);
                }

                var verificationResult = new OtpVerificationResult
                {
                    Success = true,
                    Message = $"Successfully executed {pendingTx.TransactionType} of ৳{pendingTx.Amount:N2}. New Balance: ৳{userAccount.Balance:N2}",
                    TrackingId = trackingId,
                    NewBalance = userAccount.Balance,
                    CompletedAt = now
                };

                _logger.LogInformation("Universal OTP Verified: Transaction {TrackingId} of type {Type} completed for user {UserId}",
                    trackingId, pendingTx.TransactionType, pendingTx.UserId);

                return (200, ApiResponse<OtpVerificationResult>.SuccessResponse(verificationResult, verificationResult.Message));
            }
            catch (Exception ex)
            {
                await dbTransaction.RollbackAsync();
                _logger.LogError(ex, "Error executing OTP verification for challenge {ChallengeId}", challengeId);
                return (500, ApiResponse<OtpVerificationResult>.FailureResponse("An unexpected error occurred while executing the transaction."));
            }
        }

        public async Task<(int StatusCode, ApiResponse<OtpChallengeResult> Response)> ResendOtpAsync(Guid challengeId)
        {
            var now = DateTime.UtcNow;

            var challenge = await _context.OtpChallenges
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.Id == challengeId);

            if (challenge == null)
            {
                return (404, ApiResponse<OtpChallengeResult>.FailureResponse("OTP challenge request not found."));
            }

            if (challenge.IsUsed || challenge.Status == "Used")
            {
                return (400, ApiResponse<OtpChallengeResult>.FailureResponse("Cannot resend OTP for an already completed transaction."));
            }

            if (challenge.Status == "Locked" || challenge.AttemptCount >= challenge.MaxAttempts)
            {
                return (400, ApiResponse<OtpChallengeResult>.FailureResponse("This challenge is locked due to multiple failed attempts. Please initiate a new transaction."));
            }

            // Rule 5: 60-Second Cooldown Check
            if (challenge.LastSentAt.HasValue)
            {
                var timeSinceLastSent = (now - challenge.LastSentAt.Value).TotalSeconds;
                if (timeSinceLastSent < 60)
                {
                    var waitSeconds = (int)Math.Ceiling(60 - timeSinceLastSent);
                    return (429, ApiResponse<OtpChallengeResult>.FailureResponse($"Please wait {waitSeconds} second(s) before requesting a new OTP."));
                }
            }

            // Generate fresh 6-digit OTP & update challenge
            var plainOtp = Generate6DigitOtp();
            challenge.HashedOtp = BCrypt.Net.BCrypt.HashPassword(plainOtp);
            challenge.IssuedAt = now;
            challenge.ExpiresAt = now.AddSeconds(120); // Reset 120s timer
            challenge.LastSentAt = now;
            challenge.Status = "Pending";

            // Update associated pending transaction if expired
            var pendingTx = await _context.PendingTransactions.FirstOrDefaultAsync(p => p.Id == challenge.TransactionId);
            if (pendingTx != null && pendingTx.Status == "Expired")
            {
                pendingTx.Status = "PendingOtp";
            }

            await _context.SaveChangesAsync();

            // Dispatch Email
            await _emailService.SendUniversalTransactionOtpAsync(
                challenge.User.Email,
                challenge.User.FullName,
                challenge.TransactionType,
                challenge.TransactionAmount,
                challenge.TargetInfo,
                pendingTx?.Reference,
                plainOtp);

            var result = new OtpChallengeResult
            {
                ChallengeId = challenge.Id,
                TransactionId = challenge.TransactionId,
                TransactionType = challenge.TransactionType,
                Amount = challenge.TransactionAmount,
                TargetInfo = challenge.TargetInfo,
                MaskedEmail = MaskEmail(challenge.User.Email),
                IssuedAt = now,
                ExpiresAt = challenge.ExpiresAt,
                CooldownRemainingSeconds = 60
            };

            return (200, ApiResponse<OtpChallengeResult>.SuccessResponse(result, $"A fresh 6-digit OTP code has been dispatched to {result.MaskedEmail}."));
        }

        public async Task InvalidateExpiredChallengesAsync()
        {
            var now = DateTime.UtcNow;

            var expiredChallenges = await _context.OtpChallenges
                .Where(c => !c.IsUsed && c.Status == "Pending" && c.ExpiresAt < now)
                .ToListAsync();

            if (expiredChallenges.Any())
            {
                foreach (var c in expiredChallenges)
                {
                    c.Status = "Expired";
                }

                var transactionIds = expiredChallenges.Select(c => c.TransactionId).ToList();
                var pendingTxs = await _context.PendingTransactions
                    .Where(p => transactionIds.Contains(p.Id) && p.Status == "PendingOtp")
                    .ToListAsync();

                foreach (var p in pendingTxs)
                {
                    p.Status = "Expired";
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation("Invalidated {Count} expired OTP challenges.", expiredChallenges.Count);
            }
        }

        public async Task<List<PendingTransaction>> GetPendingTransactionsAsync(int userId)
        {
            return await _context.PendingTransactions
                .Where(p => p.UserId == userId && p.Status == "PendingOtp")
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
        }
    }
}
