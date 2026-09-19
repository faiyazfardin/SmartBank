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
using SmartBank.Services.Interfaces;

namespace SmartBank.Controllers
{
    [ApiController]
    [Route("api/transactions")]
    [Route("api/transfers")]
    [Authorize]
    public class TransactionApiController : ControllerBase
    {
        private readonly SmartBankDbContext _context;
        private readonly ITransferService _transferService;

        public TransactionApiController(SmartBankDbContext context, ITransferService transferService)
        {
            _context = context;
            _transferService = transferService;
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

        public class TransferApiRequest
        {
            public string RecipientAccountNumber { get; set; } = string.Empty;
            public decimal Amount { get; set; }
            public string? Memo { get; set; }
        }

        public class VerifyTransferOtpApiRequest
        {
            public string Otp { get; set; } = string.Empty;
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

        // POST: api/transactions/transfer or api/transfers -> Initiates pending transfer & dispatches email OTP
        [HttpPost]
        [HttpPost("transfer")]
        public async Task<IActionResult> Transfer([FromBody] TransferApiRequest request)
        {
            var userId = GetCurrentUserId();
            var (statusCode, response) = await _transferService.InitiateTransferAsync(
                userId, request.RecipientAccountNumber, request.Amount, request.Memo);

            return StatusCode(statusCode, response);
        }

        // POST: api/transfers/{id}/verify-otp or api/transactions/transfer/{id}/verify-otp
        [HttpPost("{id}/verify-otp")]
        [HttpPost("transfer/{id}/verify-otp")]
        public async Task<IActionResult> VerifyTransferOtp(int id, [FromBody] VerifyTransferOtpApiRequest request)
        {
            var userId = GetCurrentUserId();
            var (statusCode, response) = await _transferService.VerifyAndCompleteTransferAsync(userId, id, request.Otp);

            return StatusCode(statusCode, response);
        }

        // POST: api/transfers/{id}/resend-otp or api/transactions/transfer/{id}/resend-otp
        [HttpPost("{id}/resend-otp")]
        [HttpPost("transfer/{id}/resend-otp")]
        public async Task<IActionResult> ResendTransferOtp(int id)
        {
            var userId = GetCurrentUserId();
            var (statusCode, response) = await _transferService.ResendTransferOtpAsync(userId, id);

            return StatusCode(statusCode, response);
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
