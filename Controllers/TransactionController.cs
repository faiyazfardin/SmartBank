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

        // Deposit
        [HttpGet]
        public IActionResult Deposit()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Deposit(decimal amount)
        {
            if (amount <= 0)
            {
                ModelState.AddModelError("", "Deposit amount must be greater than zero.");
                return View();
            }

            var userId = GetCurrentUserId();
            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.UserId == userId);

            if (account == null)
            {
                ModelState.AddModelError("", "No account found.");
                return View();
            }

            if (!account.IsActive)
            {
                ModelState.AddModelError("", "This account is frozen. Contact support.");
                return View();
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

            TempData["Message"] = $"Successfully deposited {amount:C}.";
            return RedirectToAction("Index", "Dashboard");
        }

        // Withdraw
        [HttpGet]
        public IActionResult Withdraw()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Withdraw(decimal amount)
        {
            if (amount <= 0)
            {
                ModelState.AddModelError("", "Withdrawal amount must be greater than zero.");
                return View();
            }

            var userId = GetCurrentUserId();
            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.UserId == userId);

            if (account == null)
            {
                ModelState.AddModelError("", "No account found.");
                return View();
            }

            if (!account.IsActive)
            {
                ModelState.AddModelError("", "This account is frozen. Contact support.");
                return View();
            }

            if (amount > account.Balance)
            {
                ModelState.AddModelError("", "Insufficient funds.");
                return View();
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

            TempData["Message"] = $"Successfully withdrew {amount:C}.";
            return RedirectToAction("Index", "Dashboard");
        }

        // Transfer
        [HttpGet]
        public IActionResult Transfer()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Transfer(string recipientAccountNumber, decimal amount)
        {
            if (amount <= 0)
            {
                ModelState.AddModelError("", "Transfer amount must be greater than zero.");
                return View();
            }

            var userId = GetCurrentUserId();
            var senderAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.UserId == userId);

            if (senderAccount == null)
            {
                ModelState.AddModelError("", "No account found.");
                return View();
            }

            if (!senderAccount.IsActive)
            {
                ModelState.AddModelError("", "Your account is frozen. Contact support.");
                return View();
            }

            var recipientAccount = await _context.Accounts
                .FirstOrDefaultAsync(a => a.AccountNumber == recipientAccountNumber.Trim());

            if (recipientAccount == null)
            {
                ModelState.AddModelError("", "Recipient account not found.");
                return View();
            }

            if (!recipientAccount.IsActive)
            {
                ModelState.AddModelError("", "The recipient's account is currently frozen and cannot receive funds.");
                return View();
            }

            if (recipientAccount.Id == senderAccount.Id)
            {
                ModelState.AddModelError("", "You cannot transfer to your own account.");
                return View();
            }

            if (amount > senderAccount.Balance)
            {
                ModelState.AddModelError("", "Insufficient funds.");
                return View();
            }

            using var dbTransaction = await _context.Database.BeginTransactionAsync();
            try
            {
                senderAccount.Balance -= amount;
                senderAccount.UpdatedAt = DateTime.UtcNow;

                recipientAccount.Balance += amount;
                recipientAccount.UpdatedAt = DateTime.UtcNow;

                var outgoing = new Transaction
                {
                    AccountId = senderAccount.Id,
                    Type = TransactionType.TransferOut,
                    Amount = amount,
                    Timestamp = DateTime.UtcNow,
                    RelatedAccountId = recipientAccount.Id
                };

                var incoming = new Transaction
                {
                    AccountId = recipientAccount.Id,
                    Type = TransactionType.TransferIn,
                    Amount = amount,
                    Timestamp = DateTime.UtcNow,
                    RelatedAccountId = senderAccount.Id
                };

                _context.Transactions.Add(outgoing);
                _context.Transactions.Add(incoming);
                await _context.SaveChangesAsync();
                await dbTransaction.CommitAsync();

                TempData["Message"] = $"Successfully transferred {amount:C} to account {recipientAccountNumber}.";
                return RedirectToAction("Index", "Dashboard");
            }
            catch (Exception ex)
            {
                await dbTransaction.RollbackAsync();
                ModelState.AddModelError("", $"Transfer failed: {ex.Message}");
                return View();
            }
        }

        // History
        [HttpGet]
        public async Task<IActionResult> History()
        {
            var userId = GetCurrentUserId();
            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.UserId == userId);

            if (account == null)
            {
                return View(new List<Transaction>());
            }

            var transactions = await _context.Transactions
                .Where(t => t.AccountId == account.Id)
                .OrderByDescending(t => t.Timestamp)
                .ToListAsync();

            return View(transactions);
        }
    }
}