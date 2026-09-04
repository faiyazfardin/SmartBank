using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartBank.Data;
using SmartBank.Models;
using System.Threading.Tasks;

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
    }
}