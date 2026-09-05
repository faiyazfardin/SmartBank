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

        // POST: Admin/EditUser
        [HttpPost]
        public async Task<IActionResult> EditUser(int userId, string fullName, string username, string email, string? phoneNumber, string role, string? accountNumber, decimal? balance, bool? isActive)
        {
            var user = await _context.Users
                .Include(u => u.Accounts)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                TempData["Error"] = "User not found.";
                return RedirectToAction("Users");
            }

            if (!string.IsNullOrWhiteSpace(username))
            {
                var normUser = username.Trim().ToLowerInvariant();
                var exists = await _context.Users.AnyAsync(u => u.Username.ToLower() == normUser && u.Id != userId);
                if (exists)
                {
                    TempData["Error"] = "Username is already taken by another account.";
                    return RedirectToAction("Users");
                }
                user.Username = normUser;
            }

            if (!string.IsNullOrWhiteSpace(email))
            {
                var normEmail = email.Trim().ToLowerInvariant();
                var exists = await _context.Users.AnyAsync(u => u.Email.ToLower() == normEmail && u.Id != userId);
                if (exists)
                {
                    TempData["Error"] = "Email is already registered by another account.";
                    return RedirectToAction("Users");
                }
                user.Email = normEmail;
            }

            if (!string.IsNullOrWhiteSpace(fullName))
            {
                user.FullName = fullName.Trim();
            }

            user.PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();

            if (!string.IsNullOrWhiteSpace(role))
            {
                user.Role = role.Trim();
            }

            var account = user.Accounts.FirstOrDefault();
            if (account != null)
            {
                if (!string.IsNullOrWhiteSpace(accountNumber) && accountNumber.Trim() != account.AccountNumber)
                {
                    var newAcc = accountNumber.Trim();
                    var exists = await _context.Accounts.AnyAsync(a => a.AccountNumber == newAcc && a.Id != account.Id);
                    if (exists)
                    {
                        TempData["Error"] = "Account number already exists.";
                        return RedirectToAction("Users");
                    }
                    account.AccountNumber = newAcc;
                }

                if (balance.HasValue && balance.Value >= 0)
                {
                    account.Balance = balance.Value;
                }

                if (isActive.HasValue)
                {
                    account.IsActive = isActive.Value;
                }

                account.UpdatedAt = DateTime.UtcNow;
            }

            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            TempData["Message"] = $"User details for {user.FullName} (@{user.Username}) updated successfully.";
            return RedirectToAction("Users");
        }
    }
}