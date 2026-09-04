using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartBank.Data;
using System.Threading.Tasks;
using System.Collections.Generic;
using SmartBank.Models;

namespace SmartBank.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminController(ApplicationDbContext context)
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
                await _context.SaveChangesAsync();
            }
            return RedirectToAction("Users");
        }

        // POST: Admin/CreateAccount
        [HttpPost]
        public async Task<IActionResult> CreateAccount(string userId)
        {
            var existingAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.UserId == userId);
            if (existingAccount != null)
            {
                TempData["Message"] = "This user already has an account.";
                return RedirectToAction("Users");
            }

            var newAccount = new Account
            {
                AccountNumber = System.Guid.NewGuid().ToString("N").Substring(0, 10),
                Balance = 0,
                IsActive = true,
                UserId = userId
            };

            _context.Accounts.Add(newAccount);
            await _context.SaveChangesAsync();

            TempData["Message"] = "Account created successfully.";
            return RedirectToAction("Users");
        }
    }
}