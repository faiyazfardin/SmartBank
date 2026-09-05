using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartBank.Data;
using SmartBank.Entities;

namespace SmartBank.Controllers
{
    [Authorize]
    public class TransactionController : Controller
    {
        private readonly SmartBankDbContext _context;

        public TransactionController(SmartBankDbContext context)
        {
            _context = context;
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

        // POST: Transaction/Deposit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Deposit(decimal amount)
        {
            if (amount <= 0)
            {
                TempData["ErrorToast"] = "Deposit amount must be greater than ৳0.";
                return RedirectToAction("Deposit");
            }

            var userId = GetCurrentUserId();
            var (account, error) = await CheckAccountAndSuspensionAsync(userId);
            if (error != null || account == null)
            {
                TempData["ErrorToast"] = error ?? "Unable to process deposit.";
                return RedirectToAction("Deposit");
            }

            account.Balance += amount;
            account.UpdatedAt = DateTime.UtcNow;

            var transaction = new Transaction
            {
                AccountId = account.Id,
                Type = TransactionType.Deposit,
                Amount = amount,
                Timestamp = DateTime.UtcNow
            };

            _context.Transactions.Add(transaction);
            await _context.SaveChangesAsync();

            TempData["SuccessToast"] = $"Successfully deposited ৳{amount:N2} to your account! New Balance: ৳{account.Balance:N2}";
            return RedirectToAction("Index", "Dashboard");
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

        // POST: Transaction/Withdraw
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Withdraw(decimal amount)
        {
            if (amount <= 0)
            {
                TempData["ErrorToast"] = "Withdrawal amount must be greater than ৳0.";
                return RedirectToAction("Withdraw");
            }

            var userId = GetCurrentUserId();
            var (account, error) = await CheckAccountAndSuspensionAsync(userId);
            if (error != null || account == null)
            {
                TempData["ErrorToast"] = error ?? "Unable to process withdrawal.";
                return RedirectToAction("Withdraw");
            }

            if (amount > account.Balance)
            {
                TempData["ErrorToast"] = $"Insufficient balance! Available balance is ৳{account.Balance:N2}.";
                return RedirectToAction("Withdraw");
            }

            account.Balance -= amount;
            account.UpdatedAt = DateTime.UtcNow;

            var transaction = new Transaction
            {
                AccountId = account.Id,
                Type = TransactionType.Withdraw,
                Amount = amount,
                Timestamp = DateTime.UtcNow
            };

            _context.Transactions.Add(transaction);
            await _context.SaveChangesAsync();

            TempData["SuccessToast"] = $"Successfully withdrew ৳{amount:N2}! Remaining Balance: ৳{account.Balance:N2}";
            return RedirectToAction("Index", "Dashboard");
        }

        // GET: Transaction/Transfer
        [HttpGet]
        public async Task<IActionResult> Transfer()
        {
            var userId = GetCurrentUserId();
            var (account, error) = await CheckAccountAndSuspensionAsync(userId);
            ViewBag.ErrorMessage = error;
            return View(account);
        }

        // POST: Transaction/Transfer
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Transfer(string recipientAccountNumber, decimal amount, string? memo)
        {
            if (string.IsNullOrWhiteSpace(recipientAccountNumber))
            {
                TempData["ErrorToast"] = "Recipient account number is required.";
                return RedirectToAction("Transfer");
            }

            if (amount <= 0)
            {
                TempData["ErrorToast"] = "Transfer amount must be greater than ৳0.";
                return RedirectToAction("Transfer");
            }

            var userId = GetCurrentUserId();
            var (senderAccount, senderError) = await CheckAccountAndSuspensionAsync(userId);
            if (senderError != null || senderAccount == null)
            {
                TempData["ErrorToast"] = senderError ?? "Sender account is unavailable.";
                return RedirectToAction("Transfer");
            }

            var recAccClean = recipientAccountNumber.Trim();
            var recipientUser = await _context.Users
                .Include(u => u.Accounts)
                .FirstOrDefaultAsync(u => u.Accounts.Any(a => a.AccountNumber == recAccClean));

            if (recipientUser == null)
            {
                TempData["ErrorToast"] = $"Recipient account '{recAccClean}' was not found in the SmartBank network.";
                return RedirectToAction("Transfer");
            }

            var recipientAccount = recipientUser.Accounts.FirstOrDefault(a => a.AccountNumber == recAccClean);
            if (recipientAccount == null)
            {
                TempData["ErrorToast"] = "Recipient account record not found.";
                return RedirectToAction("Transfer");
            }

            if (recipientAccount.Id == senderAccount.Id)
            {
                TempData["ErrorToast"] = "Self-transfers are not allowed. Please enter a different recipient account.";
                return RedirectToAction("Transfer");
            }

            if (recipientUser.Status != null && recipientUser.Status.Equals("Suspended", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorToast"] = "Transfer failed: The recipient account is under administrative suspension.";
                return RedirectToAction("Transfer");
            }

            if (!recipientAccount.IsActive)
            {
                TempData["ErrorToast"] = "Transfer failed: The recipient account is frozen and cannot receive incoming funds.";
                return RedirectToAction("Transfer");
            }

            if (amount > senderAccount.Balance)
            {
                TempData["ErrorToast"] = $"Insufficient balance! You have ৳{senderAccount.Balance:N2} available.";
                return RedirectToAction("Transfer");
            }

            using var dbTransaction = await _context.Database.BeginTransactionAsync();
            try
            {
                senderAccount.Balance -= amount;
                senderAccount.UpdatedAt = DateTime.UtcNow;

                recipientAccount.Balance += amount;
                recipientAccount.UpdatedAt = DateTime.UtcNow;

                var outTx = new Transaction
                {
                    AccountId = senderAccount.Id,
                    Type = TransactionType.TransferOut,
                    Amount = amount,
                    Timestamp = DateTime.UtcNow,
                    RelatedAccountId = recipientAccount.Id
                };

                var inTx = new Transaction
                {
                    AccountId = recipientAccount.Id,
                    Type = TransactionType.TransferIn,
                    Amount = amount,
                    Timestamp = DateTime.UtcNow,
                    RelatedAccountId = senderAccount.Id
                };

                _context.Transactions.Add(outTx);
                _context.Transactions.Add(inTx);

                await _context.SaveChangesAsync();
                await dbTransaction.CommitAsync();

                TempData["SuccessToast"] = $"Successfully transferred ৳{amount:N2} to {recipientUser.FullName} ({recAccClean})!";
                return RedirectToAction("Receipt", new { id = outTx.Id });
            }
            catch (Exception ex)
            {
                await dbTransaction.RollbackAsync();
                TempData["ErrorToast"] = $"Transfer processing error: {ex.Message}";
                return RedirectToAction("Transfer");
            }
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

        // POST: Transaction/PayBill
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PayBill(string billerCategory, string billerName, string billNumber, decimal amount)
        {
            if (string.IsNullOrWhiteSpace(billerName) || string.IsNullOrWhiteSpace(billNumber))
            {
                TempData["ErrorToast"] = "Please provide both biller details and account/meter number.";
                return RedirectToAction("PayBill");
            }

            if (amount <= 0)
            {
                TempData["ErrorToast"] = "Bill payment amount must be greater than ৳0.";
                return RedirectToAction("PayBill");
            }

            var userId = GetCurrentUserId();
            var (account, error) = await CheckAccountAndSuspensionAsync(userId);
            if (error != null || account == null)
            {
                TempData["ErrorToast"] = error ?? "Unable to process payment.";
                return RedirectToAction("PayBill");
            }

            if (amount > account.Balance)
            {
                TempData["ErrorToast"] = $"Insufficient balance! Available balance is ৳{account.Balance:N2}.";
                return RedirectToAction("PayBill");
            }

            account.Balance -= amount;
            account.UpdatedAt = DateTime.UtcNow;

            var transaction = new Transaction
            {
                AccountId = account.Id,
                Type = TransactionType.Withdraw, // Utility bill treated as debit
                Amount = amount,
                Timestamp = DateTime.UtcNow
            };

            _context.Transactions.Add(transaction);
            await _context.SaveChangesAsync();

            TempData["SuccessToast"] = $"Successfully paid ৳{amount:N2} to {billerName} (Ref: {billNumber})!";
            return RedirectToAction("Receipt", new { id = transaction.Id, biller = billerName, billRef = billNumber, category = billerCategory });
        }

        // GET: Transaction/History
        [HttpGet]
        public async Task<IActionResult> History(string? type, string? search)
        {
            var userId = GetCurrentUserId();
            var user = await _context.Users
                .Include(u => u.Accounts)
                .FirstOrDefaultAsync(u => u.Id == userId);

            var account = user?.Accounts.FirstOrDefault();
            if (account == null)
            {
                return View(new List<Transaction>());
            }

            var query = _context.Transactions
                .Where(t => t.AccountId == account.Id);

            if (!string.IsNullOrWhiteSpace(type))
            {
                if (Enum.TryParse<TransactionType>(type, true, out var tType))
                {
                    query = query.Where(t => t.Type == tType);
                }
            }

            var transactions = await query
                .OrderByDescending(t => t.Timestamp)
                .ToListAsync();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                transactions = transactions
                    .Where(t => t.Id.ToString().Contains(s, StringComparison.OrdinalIgnoreCase)
                             || t.Amount.ToString().Contains(s, StringComparison.OrdinalIgnoreCase)
                             || t.Type.ToString().Contains(s, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            ViewBag.SelectedType = type;
            ViewBag.Search = search;
            ViewBag.Account = account;
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

            if (user == null || !user.Accounts.Any())
            {
                return RedirectToAction("Index", "Dashboard");
            }

            var account = user.Accounts.First();
            var start = fromDate ?? DateTime.UtcNow.AddMonths(-1);
            var end = toDate ?? DateTime.UtcNow;

            var transactions = await _context.Transactions
                .Where(t => t.AccountId == account.Id && t.Timestamp >= start && t.Timestamp <= end.AddDays(1))
                .OrderByDescending(t => t.Timestamp)
                .ToListAsync();

            var totalCredits = transactions
                .Where(t => t.Type == TransactionType.Deposit || t.Type == TransactionType.TransferIn)
                .Sum(t => t.Amount);

            var totalDebits = transactions
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

        // GET: Transaction/Receipt
        [HttpGet]
        public async Task<IActionResult> Receipt(int id, string? biller = null, string? billRef = null, string? category = null)
        {
            var userId = GetCurrentUserId();
            var user = await _context.Users
                .Include(u => u.Accounts)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null || !user.Accounts.Any())
            {
                return RedirectToAction("Index", "Dashboard");
            }

            var account = user.Accounts.First();
            var transaction = await _context.Transactions
                .FirstOrDefaultAsync(t => t.Id == id && t.AccountId == account.Id);

            if (transaction == null)
            {
                TempData["ErrorToast"] = "Transaction record not found.";
                return RedirectToAction("History");
            }

            ViewBag.User = user;
            ViewBag.Account = account;
            ViewBag.Biller = biller;
            ViewBag.BillRef = billRef;
            ViewBag.Category = category;

            return View(transaction);
        }
    }
}