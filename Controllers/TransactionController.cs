using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartBank.Data;
using SmartBank.Models;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace SmartBank.Controllers
{
    [Authorize]
    public class TransactionController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public TransactionController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        //diposit
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

            var currentUser = await _userManager.GetUserAsync(User);
            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.UserId == currentUser.Id);

            if (account == null)
            {
                ModelState.AddModelError("", "No account found.");
                return View();
            }

            account.Balance += amount;

            var transaction = new Transaction
            {
                AccountId = account.Id,
                Type = TransactionType.Deposit,
                Amount = amount,
                Timestamp = System.DateTime.Now
            };

            _context.Transactions.Add(transaction);
            await _context.SaveChangesAsync();

            TempData["Message"] = $"Successfully deposited {amount:C}.";
            return RedirectToAction("Index", "Dashboard");
        }

        //withdraw
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

            var currentUser = await _userManager.GetUserAsync(User);
            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.UserId == currentUser.Id);

            if (account == null)
            {
                ModelState.AddModelError("", "No account found.");
                return View();
            }

            if (amount > account.Balance)
            {
                ModelState.AddModelError("", "Insufficient funds.");
                return View();
            }

            account.Balance -= amount;

            var transaction = new Transaction
            {
                AccountId = account.Id,
                Type = TransactionType.Withdraw,
                Amount = amount,
                Timestamp = System.DateTime.Now
            };

            _context.Transactions.Add(transaction);
            await _context.SaveChangesAsync();

            TempData["Message"] = $"Successfully withdrew {amount:C}.";
            return RedirectToAction("Index", "Dashboard");
        }

        //Money transfer
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

            var currentUser = await _userManager.GetUserAsync(User);
            var senderAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.UserId == currentUser.Id);

            if (senderAccount == null)
            {
                ModelState.AddModelError("", "No account found.");
                return View();
            }

            var recipientAccount = await _context.Accounts
                .FirstOrDefaultAsync(a => a.AccountNumber == recipientAccountNumber);

            if (recipientAccount == null)
            {
                ModelState.AddModelError("", "Recipient account not found.");
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

            senderAccount.Balance -= amount;
            recipientAccount.Balance += amount;

            var outgoing = new Transaction
            {
                AccountId = senderAccount.Id,
                Type = TransactionType.TransferOut,
                Amount = amount,
                Timestamp = System.DateTime.Now,
                RelatedAccountId = recipientAccount.Id
            };

            var incoming = new Transaction
            {
                AccountId = recipientAccount.Id,
                Type = TransactionType.TransferIn,
                Amount = amount,
                Timestamp = System.DateTime.Now,
                RelatedAccountId = senderAccount.Id
            };

            _context.Transactions.Add(outgoing);
            _context.Transactions.Add(incoming);
            await _context.SaveChangesAsync();

            TempData["Message"] = $"Successfully transferred {amount:C} to account {recipientAccountNumber}.";
            return RedirectToAction("Index", "Dashboard");
        }

        //Free king History
        [HttpGet]
        public async Task<IActionResult> History()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.UserId == currentUser.Id);

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