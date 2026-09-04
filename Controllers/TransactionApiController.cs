using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartBank.Data;
using SmartBank.DTOs.Common;
using SmartBank.Entities;

namespace SmartBank.Controllers
{
    [ApiController]
    [Route("api/transactions")]
    [Authorize]
    public class TransactionApiController : ControllerBase
    {
        private readonly SmartBankDbContext _context;

        public TransactionApiController(SmartBankDbContext context)
        {
            _context = context;
        }

        private int GetCurrentUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            return int.TryParse(claim, out var id) ? id : 0;
        }

        public class AmountRequest
        {
            public decimal Amount { get; set; }
        }

        public class TransferRequest
        {
            public string RecipientAccountNumber { get; set; } = string.Empty;
            public decimal Amount { get; set; }
        }

        private async Task<(Account? Account, string? ErrorMessage)> CheckSuspensionAndAccountAsync(int userId)
        {
            var user = await _context.Users.Include(u => u.Accounts).FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                return (null, "User record not found");
            }

            var account = user.Accounts.FirstOrDefault();
            if (account == null)
            {
                return (null, "Account record not found");
            }

            var now = DateTime.UtcNow;
            if (user.Status != null && user.Status.Equals("Suspended", StringComparison.OrdinalIgnoreCase))
            {
                if (user.LockedUntil.HasValue && user.LockedUntil.Value > now && user.LockedUntil.Value < now.AddYears(10))
                {
                    var timeRemaining = user.LockedUntil.Value - now;
                    var timeStr = timeRemaining.TotalDays >= 1 
                        ? $"{(int)timeRemaining.TotalDays} day(s) and {timeRemaining.Hours} hour(s)" 
                        : $"{(int)timeRemaining.TotalHours} hour(s) and {timeRemaining.Minutes} minute(s)";
                    return (null, $"Transaction declined: This account is suspended by Bank Administration until {user.LockedUntil.Value:MMM dd, yyyy HH:mm} UTC (approx. {timeStr} remaining). No transactions are granted.");
                }
                return (null, "Transaction declined: This account has been placed under indefinite administrative suspension. All debit and credit transactions are prohibited.");
            }

            if (!account.IsActive)
            {
                return (null, "Transaction declined: This account is currently frozen. Please contact customer support.");
            }

            return (account, null);
        }

        [HttpPost("deposit")]
        public async Task<IActionResult> Deposit([FromBody] AmountRequest request)
        {
            if (request.Amount <= 0)
            {
                return BadRequest(ApiResponse<decimal>.FailureResponse("Deposit amount must be greater than zero"));
            }

            var userId = GetCurrentUserId();
            var (account, errorMsg) = await CheckSuspensionAndAccountAsync(userId);
            if (errorMsg != null || account == null)
            {
                return StatusCode(403, ApiResponse<decimal>.FailureResponse(errorMsg ?? "Account unavailable"));
            }

            account.Balance += request.Amount;
            account.UpdatedAt = DateTime.UtcNow;

            var transaction = new Transaction
            {
                AccountId = account.Id,
                Type = TransactionType.Deposit,
                Amount = request.Amount,
                Timestamp = DateTime.UtcNow
            };

            _context.Transactions.Add(transaction);
            await _context.SaveChangesAsync();

            return Ok(ApiResponse<decimal>.SuccessResponse(account.Balance, $"Successfully deposited {request.Amount:C}"));
        }

        [HttpPost("withdraw")]
        public async Task<IActionResult> Withdraw([FromBody] AmountRequest request)
        {
            if (request.Amount <= 0)
            {
                return BadRequest(ApiResponse<decimal>.FailureResponse("Withdrawal amount must be greater than zero"));
            }

            var userId = GetCurrentUserId();
            var (account, errorMsg) = await CheckSuspensionAndAccountAsync(userId);
            if (errorMsg != null || account == null)
            {
                return StatusCode(403, ApiResponse<decimal>.FailureResponse(errorMsg ?? "Account unavailable"));
            }

            if (request.Amount > account.Balance)
            {
                return BadRequest(ApiResponse<decimal>.FailureResponse("Insufficient funds"));
            }

            account.Balance -= request.Amount;
            account.UpdatedAt = DateTime.UtcNow;

            var transaction = new Transaction
            {
                AccountId = account.Id,
                Type = TransactionType.Withdraw,
                Amount = request.Amount,
                Timestamp = DateTime.UtcNow
            };

            _context.Transactions.Add(transaction);
            await _context.SaveChangesAsync();

            return Ok(ApiResponse<decimal>.SuccessResponse(account.Balance, $"Successfully withdrew {request.Amount:C}"));
        }

        [HttpPost("transfer")]
        public async Task<IActionResult> Transfer([FromBody] TransferRequest request)
        {
            if (request.Amount <= 0)
            {
                return BadRequest(ApiResponse<decimal>.FailureResponse("Transfer amount must be greater than zero"));
            }

            var userId = GetCurrentUserId();
            var (senderAccount, senderError) = await CheckSuspensionAndAccountAsync(userId);
            if (senderError != null || senderAccount == null)
            {
                return StatusCode(403, ApiResponse<decimal>.FailureResponse(senderError ?? "Sender account unavailable"));
            }

            var recipientUser = await _context.Users.Include(u => u.Accounts).FirstOrDefaultAsync(u => u.Accounts.Any(a => a.AccountNumber == request.RecipientAccountNumber.Trim()));
            if (recipientUser == null)
            {
                return NotFound(ApiResponse<decimal>.FailureResponse("Recipient account not found"));
            }

            var recipientAccount = recipientUser.Accounts.FirstOrDefault(a => a.AccountNumber == request.RecipientAccountNumber.Trim());
            if (recipientAccount == null)
            {
                return NotFound(ApiResponse<decimal>.FailureResponse("Recipient account not found"));
            }

            if (recipientUser.Status != null && recipientUser.Status.Equals("Suspended", StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(403, ApiResponse<decimal>.FailureResponse("Transfer declined: The recipient account is under administrative suspension."));
            }

            if (!recipientAccount.IsActive)
            {
                return StatusCode(403, ApiResponse<decimal>.FailureResponse("Recipient account is frozen and cannot receive transfers"));
            }

            if (recipientAccount.Id == senderAccount.Id)
            {
                return BadRequest(ApiResponse<decimal>.FailureResponse("You cannot transfer money to your own account"));
            }

            if (request.Amount > senderAccount.Balance)
            {
                return BadRequest(ApiResponse<decimal>.FailureResponse("Insufficient funds"));
            }

            using var dbTransaction = await _context.Database.BeginTransactionAsync();
            try
            {
                senderAccount.Balance -= request.Amount;
                senderAccount.UpdatedAt = DateTime.UtcNow;

                recipientAccount.Balance += request.Amount;
                recipientAccount.UpdatedAt = DateTime.UtcNow;

                var outTx = new Transaction
                {
                    AccountId = senderAccount.Id,
                    Type = TransactionType.TransferOut,
                    Amount = request.Amount,
                    Timestamp = DateTime.UtcNow,
                    RelatedAccountId = recipientAccount.Id
                };

                var inTx = new Transaction
                {
                    AccountId = recipientAccount.Id,
                    Type = TransactionType.TransferIn,
                    Amount = request.Amount,
                    Timestamp = DateTime.UtcNow,
                    RelatedAccountId = senderAccount.Id
                };

                _context.Transactions.Add(outTx);
                _context.Transactions.Add(inTx);

                await _context.SaveChangesAsync();
                await dbTransaction.CommitAsync();

                return Ok(ApiResponse<decimal>.SuccessResponse(senderAccount.Balance, $"Successfully transferred {request.Amount:C} to account {request.RecipientAccountNumber}"));
            }
            catch (Exception ex)
            {
                await dbTransaction.RollbackAsync();
                return StatusCode(500, ApiResponse<decimal>.FailureResponse("Transfer failed", ex.Message));
            }
        }

        public class PayBillRequest
        {
            public string BillerName { get; set; } = string.Empty;
            public string BillType { get; set; } = string.Empty;
            public string ReferenceNumber { get; set; } = string.Empty;
            public decimal Amount { get; set; }
        }

        [HttpPost("pay-bill")]
        public async Task<IActionResult> PayBill([FromBody] PayBillRequest request)
        {
            if (request.Amount <= 0)
            {
                return BadRequest(ApiResponse<decimal>.FailureResponse("Bill payment amount must be greater than zero"));
            }

            var userId = GetCurrentUserId();
            var (account, errorMsg) = await CheckSuspensionAndAccountAsync(userId);
            if (errorMsg != null || account == null)
            {
                return StatusCode(403, ApiResponse<decimal>.FailureResponse(errorMsg ?? "Account unavailable"));
            }

            if (request.Amount > account.Balance)
            {
                return BadRequest(ApiResponse<decimal>.FailureResponse("Insufficient funds to pay this bill"));
            }

            account.Balance -= request.Amount;
            account.UpdatedAt = DateTime.UtcNow;

            var transaction = new Transaction
            {
                AccountId = account.Id,
                Type = TransactionType.Withdraw,
                Amount = request.Amount,
                Timestamp = DateTime.UtcNow
            };

            _context.Transactions.Add(transaction);
            await _context.SaveChangesAsync();

            return Ok(ApiResponse<decimal>.SuccessResponse(account.Balance, $"Successfully paid {request.Amount:C} for {request.BillerName} ({request.BillType}) - Ref: {request.ReferenceNumber}"));
        }

        public class TransactionDto
        {
            public int Id { get; set; }
            public int AccountId { get; set; }
            public int Type { get; set; }
            public string TypeName { get; set; } = string.Empty;
            public decimal Amount { get; set; }
            public DateTime Timestamp { get; set; }
            public int? RelatedAccountId { get; set; }
        }

        [HttpGet("history")]
        public async Task<IActionResult> GetHistory()
        {
            var userId = GetCurrentUserId();
            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.UserId == userId);

            if (account == null)
            {
                return Ok(ApiResponse<List<TransactionDto>>.SuccessResponse(new List<TransactionDto>()));
            }

            var history = await _context.Transactions
                .Where(t => t.AccountId == account.Id)
                .OrderByDescending(t => t.Timestamp)
                .Select(t => new TransactionDto
                {
                    Id = t.Id,
                    AccountId = t.AccountId,
                    Type = (int)t.Type,
                    TypeName = t.Type.ToString(),
                    Amount = t.Amount,
                    Timestamp = t.Timestamp,
                    RelatedAccountId = t.RelatedAccountId
                })
                .ToListAsync();

            return Ok(ApiResponse<List<TransactionDto>>.SuccessResponse(history));
        }
    }
}
