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
using SmartBank.DTOs.Transactions;
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
        private readonly IOtpTransactionService _otpTransactionService;

        public TransactionApiController(
            SmartBankDbContext context,
            IOtpTransactionService otpTransactionService)
        {
            _context = context;
            _otpTransactionService = otpTransactionService;
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

        public class PayBillApiRequest
        {
            public string BillerName { get; set; } = string.Empty;
            public string? BillType { get; set; }
            public string? ReferenceNumber { get; set; }
            public decimal Amount { get; set; }
        }

        // POST: api/transactions/deposit/initiate (and legacy POST api/transactions/deposit)
        [HttpPost("deposit/initiate")]
        [HttpPost("deposit")]
        public async Task<IActionResult> InitiateDeposit([FromBody] AmountRequest request)
        {
            var userId = GetCurrentUserId();
            var (statusCode, response) = await _otpTransactionService.InitiateTransactionAsync(userId, new TransactionRequestDto
            {
                TransactionType = "Deposit",
                Amount = request.Amount,
                Reference = "Account Deposit Credit"
            });

            return StatusCode(statusCode, response);
        }

        // POST: api/transactions/withdraw/initiate (and legacy POST api/transactions/withdraw)
        [HttpPost("withdraw/initiate")]
        [HttpPost("withdraw")]
        public async Task<IActionResult> InitiateWithdraw([FromBody] AmountRequest request)
        {
            var userId = GetCurrentUserId();
            var (statusCode, response) = await _otpTransactionService.InitiateTransactionAsync(userId, new TransactionRequestDto
            {
                TransactionType = "Withdraw",
                Amount = request.Amount,
                Reference = "ATM Cash Withdrawal"
            });

            return StatusCode(statusCode, response);
        }

        // POST: api/transactions/transfer/initiate (and legacy POST api/transactions/transfer)
        [HttpPost("transfer/initiate")]
        [HttpPost("transfer")]
        public async Task<IActionResult> InitiateTransfer([FromBody] TransferApiRequest request)
        {
            var userId = GetCurrentUserId();
            var (statusCode, response) = await _otpTransactionService.InitiateTransactionAsync(userId, new TransactionRequestDto
            {
                TransactionType = "Transfer",
                Amount = request.Amount,
                RecipientAccount = request.RecipientAccountNumber,
                Reference = request.Memo
            });

            return StatusCode(statusCode, response);
        }

        // POST: api/transactions/bills/initiate (and legacy POST api/transactions/pay-bill)
        [HttpPost("bills/initiate")]
        [HttpPost("pay-bill")]
        public async Task<IActionResult> InitiateBillPayment([FromBody] PayBillApiRequest request)
        {
            var userId = GetCurrentUserId();
            var (statusCode, response) = await _otpTransactionService.InitiateTransactionAsync(userId, new TransactionRequestDto
            {
                TransactionType = "BillPayment",
                Amount = request.Amount,
                BillerName = request.BillerName,
                Reference = string.IsNullOrWhiteSpace(request.BillType) 
                    ? request.ReferenceNumber 
                    : $"{request.BillType} - Ref: {request.ReferenceNumber}"
            });

            return StatusCode(statusCode, response);
        }

        // POST: api/transactions/otp/verify
        [HttpPost("otp/verify")]
        public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequestDto request)
        {
            var (statusCode, response) = await _otpTransactionService.VerifyOtpAndCommitAsync(request.ChallengeId, request.Otp);
            return StatusCode(statusCode, response);
        }

        // POST: api/transactions/otp/resend
        [HttpPost("otp/resend")]
        public async Task<IActionResult> ResendOtp([FromBody] ResendOtpRequestDto request)
        {
            var (statusCode, response) = await _otpTransactionService.ResendOtpAsync(request.ChallengeId);
            return StatusCode(statusCode, response);
        }

        // GET: api/transactions/pending
        [HttpGet("pending")]
        public async Task<IActionResult> GetPendingTransactions()
        {
            var userId = GetCurrentUserId();
            var pendingTxs = await _otpTransactionService.GetPendingTransactionsAsync(userId);
            return Ok(ApiResponse<List<PendingTransaction>>.SuccessResponse(pendingTxs));
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

        // GET: api/transactions/history
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
