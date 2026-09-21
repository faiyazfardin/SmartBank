using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartBank.Data;
using SmartBank.DTOs.Transactions;
using SmartBank.Entities;
using SmartBank.Services.Interfaces;

namespace SmartBank.Controllers
{
    [Authorize]
    public class TransactionController : Controller
    {
        private readonly SmartBankDbContext _context;
        private readonly IOtpTransactionService _otpTransactionService;

        public TransactionController(
            SmartBankDbContext context,
            IOtpTransactionService otpTransactionService)
        {
            _context = context;
            _otpTransactionService = otpTransactionService;
        }

        private int GetCurrentUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
            return int.TryParse(claim, out var id) ? id : 0;
        }

        private async Task<(Account? Account, string? ErrorMessage)> CheckAccountAndSuspensionAsync(int userId)
        {
            var user = await _context.Users
                .Include(u => u.Accounts)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                return (null, "User record not found.");
            }

            var account = user.Accounts.FirstOrDefault();
            if (account == null)
            {
                return (null, "No active banking account found for this user.");
            }

            var now = DateTime.UtcNow;
            if (user.Status != null && user.Status.Equals("Suspended", StringComparison.OrdinalIgnoreCase))
            {
                if (user.LockedUntil.HasValue && user.LockedUntil.Value > now)
                {
                    return (null, $"Your account is temporarily suspended by Bank Administration until {user.LockedUntil.Value:MMM dd, yyyy HH:mm} UTC. Transactions are prohibited.");
                }
                return (null, "Your account has been placed under administrative suspension. Transactions are prohibited.");
            }

            if (!account.IsActive)
            {
                return (null, "This account is currently frozen. Please contact customer support.");
            }

            return (account, null);
        }

        // GET: Transaction/Deposit
        [HttpGet]
        public async Task<IActionResult> Deposit()
        {
            var userId = GetCurrentUserId();
            var (account, error) = await CheckAccountAndSuspensionAsync(userId);
            ViewBag.ErrorMessage = error;
            return View(account);
        }

        // POST: Transaction/Deposit -> Initiates Pending Transaction & Dispatches OTP
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Deposit(decimal amount)
        {
            var userId = GetCurrentUserId();
            var (statusCode, response) = await _otpTransactionService.InitiateTransactionAsync(userId, new TransactionRequestDto
            {
                TransactionType = "Deposit",
                Amount = amount,
                Reference = "Account Deposit Credit"
            });

            if (statusCode != 200 || response.Data == null)
            {
                TempData["ErrorToast"] = response.Message ?? "Failed to initiate deposit.";
                return RedirectToAction("Deposit");
            }

            return RedirectToAction("VerifyOtp", new { challengeId = response.Data.ChallengeId });
        }

        // GET: Transaction/Withdraw
        [HttpGet]
        public async Task<IActionResult> Withdraw()
        {
            var userId = GetCurrentUserId();
            var (account, error) = await CheckAccountAndSuspensionAsync(userId);
            ViewBag.ErrorMessage = error;
            return View(account);
        }

        // POST: Transaction/Withdraw -> Initiates Pending Transaction & Dispatches OTP
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Withdraw(decimal amount)
        {
            var userId = GetCurrentUserId();
            var (statusCode, response) = await _otpTransactionService.InitiateTransactionAsync(userId, new TransactionRequestDto
            {
                TransactionType = "Withdraw",
                Amount = amount,
                Reference = "ATM Cash Withdrawal"
            });

            if (statusCode != 200 || response.Data == null)
            {
                TempData["ErrorToast"] = response.Message ?? "Failed to initiate withdrawal.";
                return RedirectToAction("Withdraw");
            }

            return RedirectToAction("VerifyOtp", new { challengeId = response.Data.ChallengeId });
        }

        // GET: Transaction/Transfer
        [HttpGet]
        public async Task<IActionResult> Transfer()
        {
            var userId = GetCurrentUserId();
            var (account, error) = await CheckAccountAndSuspensionAsync(userId);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

            ViewBag.ErrorMessage = error;
            ViewBag.IsEmailVerified = user?.IsEmailVerified ?? false;
            ViewBag.UserEmail = user?.Email ?? "";
            return View(account);
        }

        // POST: Transaction/Transfer -> Initiates Pending Transfer & Dispatches OTP
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Transfer(string recipientAccountNumber, decimal amount, string? memo)
        {
            var userId = GetCurrentUserId();
            var (statusCode, response) = await _otpTransactionService.InitiateTransactionAsync(userId, new TransactionRequestDto
            {
                TransactionType = "Transfer",
                Amount = amount,
                RecipientAccount = recipientAccountNumber,
                Reference = memo
            });

            if (statusCode != 200 || response.Data == null)
            {
                TempData["ErrorToast"] = response.Message ?? "Failed to initiate transfer.";
                return RedirectToAction("Transfer");
            }

            return RedirectToAction("VerifyOtp", new { challengeId = response.Data.ChallengeId });
        }

        // GET: Transaction/PayBill
        [HttpGet]
        public async Task<IActionResult> PayBill()
        {
            var userId = GetCurrentUserId();
            var (account, error) = await CheckAccountAndSuspensionAsync(userId);
            ViewBag.ErrorMessage = error;
            return View(account);
        }

        // POST: Transaction/PayBill -> Initiates Pending Bill Payment & Dispatches OTP
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PayBill(string billerName, string billType, string referenceNumber, decimal amount)
        {
            var userId = GetCurrentUserId();
            var (statusCode, response) = await _otpTransactionService.InitiateTransactionAsync(userId, new TransactionRequestDto
            {
                TransactionType = "BillPayment",
                Amount = amount,
                BillerName = billerName,
                Reference = $"{billType} - Ref: {referenceNumber}"
            });

            if (statusCode != 200 || response.Data == null)
            {
                TempData["ErrorToast"] = response.Message ?? "Failed to initiate bill payment.";
                return RedirectToAction("PayBill");
            }

            return RedirectToAction("VerifyOtp", new { challengeId = response.Data.ChallengeId });
        }

        // GET: Transaction/VerifyOtp?challengeId={guid}
        [HttpGet]
        public async Task<IActionResult> VerifyOtp(Guid challengeId)
        {
            var userId = GetCurrentUserId();
            var challenge = await _context.OtpChallenges
                .Include(c => c.User)
                .Include(c => c.PendingTransaction)
                .FirstOrDefaultAsync(c => c.Id == challengeId && c.UserId == userId);

            if (challenge == null || challenge.IsUsed || challenge.Status == "Used" || challenge.Status == "Expired")
            {
                TempData["ErrorToast"] = "No active pending OTP challenge found.";
                return RedirectToAction("Index", "Dashboard");
            }

            ViewBag.ChallengeId = challenge.Id;
            ViewBag.TransactionType = challenge.TransactionType;
            ViewBag.Amount = challenge.TransactionAmount;
            ViewBag.TargetInfo = challenge.TargetInfo;
            ViewBag.MaskedEmail = challenge.User != null ? (challenge.User.Email.Length > 4 ? $"{challenge.User.Email[0]}***@{challenge.User.Email.Split('@')[1]}" : challenge.User.Email) : "your email";
            ViewBag.ExpiresAt = challenge.ExpiresAt;

            return View(challenge);
        }

        // POST: Transaction/VerifyOtp
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyOtp(Guid challengeId, string otp)
        {
            var (statusCode, response) = await _otpTransactionService.VerifyOtpAndCommitAsync(challengeId, otp);

            if (statusCode != 200 || !response.Success || response.Data == null)
            {
                ViewBag.ErrorMessage = response.Message ?? "Invalid OTP verification code.";
                
                var userId = GetCurrentUserId();
                var challenge = await _context.OtpChallenges
                    .Include(c => c.User)
                    .Include(c => c.PendingTransaction)
                    .FirstOrDefaultAsync(c => c.Id == challengeId && c.UserId == userId);

                if (challenge == null || challenge.Status == "Expired" || challenge.Status == "Locked")
                {
                    TempData["ErrorToast"] = response.Message ?? "OTP challenge invalid or expired.";
                    return RedirectToAction("Index", "Dashboard");
                }

                ViewBag.ChallengeId = challenge.Id;
                ViewBag.TransactionType = challenge.TransactionType;
                ViewBag.Amount = challenge.TransactionAmount;
                ViewBag.TargetInfo = challenge.TargetInfo;
                ViewBag.MaskedEmail = challenge.User != null ? (challenge.User.Email.Length > 4 ? $"{challenge.User.Email[0]}***@{challenge.User.Email.Split('@')[1]}" : challenge.User.Email) : "your email";
                ViewBag.ExpiresAt = challenge.ExpiresAt;

                return View(challenge);
            }

            TempData["SuccessToast"] = response.Message;
            return RedirectToAction("Success", new { trackingId = response.Data.TrackingId });
        }

        // GET: Transaction/Success?trackingId={id}
        [HttpGet]
        public async Task<IActionResult> Success(string trackingId)
        {
            var userId = GetCurrentUserId();
            var pendingTx = await _context.PendingTransactions
                .Include(p => p.Account)
                .FirstOrDefaultAsync(p => p.TrackingId == trackingId && p.UserId == userId);

            if (pendingTx == null)
            {
                return RedirectToAction("Index", "Dashboard");
            }

            ViewBag.TrackingId = trackingId;
            ViewBag.TransactionType = pendingTx.TransactionType;
            ViewBag.Amount = pendingTx.Amount;
            ViewBag.NewBalance = pendingTx.Account?.Balance ?? 0m;
            ViewBag.CompletedAt = pendingTx.CompletedAt ?? DateTime.UtcNow;

            return View(pendingTx);
        }

        // GET: Transaction/History
        [HttpGet]
        public async Task<IActionResult> History(string? type, string? search)
        {
            var userId = GetCurrentUserId();
            var user = await _context.Users
                .Include(u => u.Accounts)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var account = user.Accounts.FirstOrDefault();
            if (account == null)
            {
                ViewBag.ErrorMessage = "No active banking account found for your profile.";
                return View(new List<Transaction>());
            }

            var query = _context.Transactions
                .Where(t => t.AccountId == account.Id)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(type) && Enum.TryParse<TransactionType>(type, true, out var parsedType))
            {
                query = query.Where(t => t.Type == parsedType);
            }

            var transactions = await query
                .OrderByDescending(t => t.Timestamp)
                .ToListAsync();

            ViewBag.Account = account;
            ViewBag.SelectedType = type;
            ViewBag.Search = search;

            return View(transactions);
        }

        // GET: Transaction/Statement
        [HttpGet]
        public async Task<IActionResult> Statement(DateTime? fromDate, DateTime? toDate)
        {
            var userId = GetCurrentUserId();
            var user = await _context.Users
                .Include(u => u.Accounts)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var account = user.Accounts.FirstOrDefault();
            if (account == null)
            {
                return RedirectToAction("Index", "Dashboard");
            }

            var start = fromDate?.Date ?? DateTime.UtcNow.AddMonths(-1).Date;
            var end = toDate?.Date.AddDays(1).AddTicks(-1) ?? DateTime.UtcNow;

            var transactions = await _context.Transactions
                .Where(t => t.AccountId == account.Id && t.Timestamp >= start && t.Timestamp <= end)
                .OrderByDescending(t => t.Timestamp)
                .ToListAsync();

            decimal totalCredits = transactions
                .Where(t => t.Type == TransactionType.Deposit || t.Type == TransactionType.TransferIn)
                .Sum(t => t.Amount);

            decimal totalDebits = transactions
                .Where(t => t.Type == TransactionType.Withdraw || t.Type == TransactionType.TransferOut)
                .Sum(t => t.Amount);

            ViewBag.User = user;
            ViewBag.Account = account;
            ViewBag.FromDate = start;
            ViewBag.ToDate = end;
            ViewBag.TotalCredits = totalCredits;
            ViewBag.TotalDebits = totalDebits;

            return View(transactions);
        }

        // GET: Transaction/Receipt/{id}
        [HttpGet]
        public async Task<IActionResult> Receipt(int id)
        {
            var userId = GetCurrentUserId();
            var user = await _context.Users
                .Include(u => u.Accounts)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var account = user.Accounts.FirstOrDefault();
            if (account == null)
            {
                return RedirectToAction("Index", "Dashboard");
            }

            var transaction = await _context.Transactions
                .FirstOrDefaultAsync(t => t.Id == id && t.AccountId == account.Id);

            if (transaction == null)
            {
                TempData["ErrorToast"] = "Transaction record not found.";
                return RedirectToAction("History");
            }

            ViewBag.User = user;
            ViewBag.Account = account;

            return View(transaction);
        }
    }
}