using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartBank.Data;
using SmartBank.Entities;

namespace SmartBank.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly SmartBankDbContext _context;

        public AdminController(SmartBankDbContext context)
        {
            _context = context;
        }

        // GET: Admin/Users
        [HttpGet]
        public async Task<IActionResult> Users()
        {
            var users = await _context.Users
                .Include(u => u.Accounts)
                .ToListAsync();

            return View(users);
        }

        // POST: Admin/ToggleAccountStatus -> Freeze/Active account
        [HttpPost]
        public async Task<IActionResult> ToggleAccountStatus(int accountId)
        {
            var account = await _context.Accounts.FindAsync(accountId);
            if (account != null)
            {
                account.IsActive = !account.IsActive;
                account.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                TempData["Message"] = $"Account {account.AccountNumber} has been {(account.IsActive ? "activated" : "frozen")}.";
            }
            return RedirectToAction("Users");
        }

        // POST: Admin/CreateAccount
        [HttpPost]
        public async Task<IActionResult> CreateAccount(int userId)
        {
            var existingAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.UserId == userId);
            if (existingAccount != null)
            {
                TempData["Message"] = "This user already has an account.";
                return RedirectToAction("Users");
            }

            var newAccount = new Account
            {
                AccountNumber = "1000" + Guid.NewGuid().ToString("N").Substring(0, 8),
                Balance = 1000.00m,
                IsActive = true,
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Accounts.Add(newAccount);
            await _context.SaveChangesAsync();

            TempData["Message"] = $"Account {newAccount.AccountNumber} created successfully.";
            return RedirectToAction("Users");
        }
    }
}