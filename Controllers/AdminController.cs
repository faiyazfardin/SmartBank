using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartBank.Data;
using SmartBank.Entities;
using SmartBank.Security;

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
        public async Task<IActionResult> Users(string? search, string? roleFilter, string? statusFilter)
        {
            var query = _context.Users
                .Include(u => u.Accounts)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(u => u.FullName.ToLower().Contains(s)
                                      || u.Username.ToLower().Contains(s)
                                      || u.Email.ToLower().Contains(s)
                                      || (u.PhoneNumber != null && u.PhoneNumber.Contains(s))
                                      || u.Accounts.Any(a => a.AccountNumber.Contains(s)));
            }

            if (!string.IsNullOrWhiteSpace(roleFilter))
            {
                query = query.Where(u => u.Role == roleFilter);
            }

            if (!string.IsNullOrWhiteSpace(statusFilter))
            {
                query = query.Where(u => u.Status == statusFilter);
            }

            var users = await query
                .OrderByDescending(u => u.CreatedAt)
                .ToListAsync();

            // Calculate system-wide KPIs
            var totalUsers = await _context.Users.CountAsync();
            var totalAccounts = await _context.Accounts.CountAsync(a => a.IsActive);
            var totalSystemBalance = await _context.Accounts.SumAsync(a => (decimal?)a.Balance) ?? 0;
            var totalTransactionsCount = await _context.Transactions.CountAsync();
            var totalTransactionVolume = await _context.Transactions.SumAsync(t => (decimal?)t.Amount) ?? 0;
            var suspendedUsersCount = await _context.Users.CountAsync(u => u.Status == "Suspended");
            var pendingUsersCount = await _context.Users.CountAsync(u => u.Status == "Pending");

            // Pending KYC / Registration Requests
            var pendingUsers = await _context.Users
                .Include(u => u.Accounts)
                .Where(u => u.Status == "Pending")
                .OrderByDescending(u => u.CreatedAt)
                .ToListAsync();

            // Platform audit recent transactions
            var recentAuditTransactions = await _context.Transactions
                .Include(t => t.Account)
                .OrderByDescending(t => t.Timestamp)
                .Take(20)
                .ToListAsync();

            ViewBag.TotalUsers = totalUsers;
            ViewBag.TotalAccounts = totalAccounts;
            ViewBag.TotalSystemBalance = totalSystemBalance;
            ViewBag.TotalTransactionsCount = totalTransactionsCount;
            ViewBag.TotalTransactionVolume = totalTransactionVolume;
            ViewBag.SuspendedUsersCount = suspendedUsersCount;
            ViewBag.PendingUsersCount = pendingUsersCount;
            ViewBag.PendingUsers = pendingUsers;
            ViewBag.AuditTransactions = recentAuditTransactions;
            ViewBag.Search = search;
            ViewBag.RoleFilter = roleFilter;
            ViewBag.StatusFilter = statusFilter;

            return View(users);
        }

        // POST: Admin/ApproveUser
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveUser(int userId)
        {
            var user = await _context.Users.Include(u => u.Accounts).FirstOrDefaultAsync(u => u.Id == userId);
            if (user != null)
            {
                user.Status = "Active";
                user.UpdatedAt = DateTime.UtcNow;

                foreach (var acc in user.Accounts)
                {
                    acc.IsActive = true;
                    acc.UpdatedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();
                TempData["SuccessToast"] = $"Account for {user.FullName} (@{user.Username}) with NID {user.NidNumber} has been APPROVED and activated!";
            }
            else
            {
                TempData["ErrorToast"] = "User not found.";
            }
            return RedirectToAction("Users");
        }

        // POST: Admin/RejectUser
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectUser(int userId)
        {
            var user = await _context.Users.Include(u => u.Accounts).FirstOrDefaultAsync(u => u.Id == userId);
            if (user != null)
            {
                user.Status = "Rejected";
                user.UpdatedAt = DateTime.UtcNow;

                foreach (var acc in user.Accounts)
                {
                    acc.IsActive = false;
                    acc.UpdatedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();
                TempData["InfoToast"] = $"Registration request for {user.FullName} (@{user.Username}) has been REJECTED.";
            }
            else
            {
                TempData["ErrorToast"] = "User not found.";
            }
            return RedirectToAction("Users");
        }

        // POST: Admin/DeleteUser
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUser(int userId)
        {
            var currentClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
            if (int.TryParse(currentClaim, out var currentAdminId) && currentAdminId == userId)
            {
                TempData["ErrorToast"] = "Administrative safety lockout: You cannot delete your own active admin account.";
                return RedirectToAction("Users");
            }

            var user = await _context.Users
                .Include(u => u.Accounts)
                .Include(u => u.RefreshTokens)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                TempData["ErrorToast"] = "User not found.";
                return RedirectToAction("Users");
            }

            var accountIds = user.Accounts.Select(a => a.Id).ToList();

            if (accountIds.Any())
            {
                var relatedTx = await _context.Transactions
                    .Where(t => accountIds.Contains(t.AccountId) || (t.RelatedAccountId.HasValue && accountIds.Contains(t.RelatedAccountId.Value)))
                    .ToListAsync();
                _context.Transactions.RemoveRange(relatedTx);
            }

            _context.Accounts.RemoveRange(user.Accounts);
            _context.RefreshTokens.RemoveRange(user.RefreshTokens);
            _context.Users.Remove(user);

            await _context.SaveChangesAsync();

            TempData["SuccessToast"] = $"User profile and account for {user.FullName} (@{user.Username}) have been permanently deleted.";
            return RedirectToAction("Users");
        }

        // POST: Admin/ToggleAccountStatus
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleAccountStatus(int accountId)
        {
            var account = await _context.Accounts.FindAsync(accountId);
            if (account != null)
            {
                account.IsActive = !account.IsActive;
                account.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                TempData["SuccessToast"] = $"Account {account.AccountNumber} has been {(account.IsActive ? "activated" : "frozen")}.";
            }
            else
            {
                TempData["ErrorToast"] = "Account not found.";
            }
            return RedirectToAction("Users");
        }

        // POST: Admin/SuspendUser
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SuspendUser(int userId, string suspensionType, int? customHours, string? reason)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                TempData["ErrorToast"] = "User not found.";
                return RedirectToAction("Users");
            }

            var now = DateTime.UtcNow;

            if (suspensionType == "Lift")
            {
                user.Status = "Active";
                user.LockedUntil = null;
                user.FailedLoginCount = 0;
                user.UpdatedAt = now;
                await _context.SaveChangesAsync();
                TempData["SuccessToast"] = $"Suspension lifted for {user.FullName} (@{user.Username}). Account is now Active.";
                return RedirectToAction("Users");
            }

            user.Status = "Suspended";
            user.UpdatedAt = now;

            if (suspensionType == "1Hour")
            {
                user.LockedUntil = now.AddHours(1);
            }
            else if (suspensionType == "24Hours")
            {
                user.LockedUntil = now.AddHours(24);
            }
            else if (suspensionType == "7Days")
            {
                user.LockedUntil = now.AddDays(7);
            }
            else if (suspensionType == "Custom" && customHours.HasValue && customHours.Value > 0)
            {
                user.LockedUntil = now.AddHours(customHours.Value);
            }
            else
            {
                // Indefinite / Permanent
                user.LockedUntil = now.AddYears(100);
            }

            await _context.SaveChangesAsync();
            TempData["SuccessToast"] = $"User {user.FullName} (@{user.Username}) has been suspended. Reason: {reason ?? "Administrative action"}";
            return RedirectToAction("Users");
        }

        // POST: Admin/CreateAccount
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAccount(int userId, decimal initialBalance = 1000m)
        {
            var user = await _context.Users.Include(u => u.Accounts).FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                TempData["ErrorToast"] = "User not found.";
                return RedirectToAction("Users");
            }

            var existingAccount = user.Accounts.FirstOrDefault();
            if (existingAccount != null)
            {
                TempData["ErrorToast"] = "This user already has a bank account.";
                return RedirectToAction("Users");
            }

            var random = new Random();
            var accNum = "1000" + random.Next(10000000, 99999999).ToString();

            var newAccount = new Account
            {
                AccountNumber = accNum,
                Balance = Math.Max(0, initialBalance),
                IsActive = true,
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Accounts.Add(newAccount);
            await _context.SaveChangesAsync();

            TempData["SuccessToast"] = $"Account {newAccount.AccountNumber} created successfully for {user.FullName} with initial balance of ৳{newAccount.Balance:N2}.";
            return RedirectToAction("Users");
        }

        // POST: Admin/CreateUser
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateUser(string fullName, string username, string email, string? phoneNumber, string? nidNumber, string password, string role, decimal initialBalance = 1000m)
        {
            if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                TempData["ErrorToast"] = "All required fields must be provided.";
                return RedirectToAction("Users");
            }

            var normUser = username.Trim().ToLowerInvariant();
            var normEmail = email.Trim().ToLowerInvariant();

            if (await _context.Users.AnyAsync(u => u.Username.ToLower() == normUser))
            {
                TempData["ErrorToast"] = "Username is already taken.";
                return RedirectToAction("Users");
            }

            if (await _context.Users.AnyAsync(u => u.Email.ToLower() == normEmail))
            {
                TempData["ErrorToast"] = "Email address is already in use.";
                return RedirectToAction("Users");
            }

            var now = DateTime.UtcNow;
            var random = new Random();
            var accNum = "1000" + random.Next(10000000, 99999999).ToString();

            var user = new User
            {
                FullName = fullName.Trim(),
                Username = normUser,
                Email = normEmail,
                PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim(),
                NidNumber = string.IsNullOrWhiteSpace(nidNumber) ? null : nidNumber.Trim(),
                PasswordHash = PasswordHasher.HashPassword(password),
                Role = string.IsNullOrWhiteSpace(role) ? "Customer" : role,
                Status = "Active",
                FailedLoginCount = 0,
                LockedUntil = null,
                CreatedAt = now,
                UpdatedAt = now
            };

            var isStaffAdmin = string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase);

            var account = new Account
            {
                AccountNumber = accNum,
                Balance = isStaffAdmin ? 0.00m : Math.Max(0, initialBalance),
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            };

            user.Accounts.Add(account);
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            TempData["SuccessToast"] = $"User {user.FullName} created successfully with Account Number {accNum}.";
            return RedirectToAction("Users");
        }

        // POST: Admin/EditUser
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditUser(int userId, string fullName, string username, string email, string? phoneNumber, string? nidNumber, string role, string status, string? accountNumber, decimal? balance, bool? isActive)
        {
            var user = await _context.Users
                .Include(u => u.Accounts)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                TempData["ErrorToast"] = "User not found.";
                return RedirectToAction("Users");
            }

            if (!string.IsNullOrWhiteSpace(username))
            {
                var normUser = username.Trim().ToLowerInvariant();
                var exists = await _context.Users.AnyAsync(u => u.Username.ToLower() == normUser && u.Id != userId);
                if (exists)
                {
                    TempData["ErrorToast"] = "Username is already taken by another user.";
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
                    TempData["ErrorToast"] = "Email is already registered to another user.";
                    return RedirectToAction("Users");
                }
                user.Email = normEmail;
            }

            if (!string.IsNullOrWhiteSpace(fullName))
            {
                user.FullName = fullName.Trim();
            }

            user.PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
            user.NidNumber = string.IsNullOrWhiteSpace(nidNumber) ? null : nidNumber.Trim();

            if (!string.IsNullOrWhiteSpace(role))
            {
                user.Role = role.Trim();
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                user.Status = status.Trim();
                if (user.Status == "Active")
                {
                    user.LockedUntil = null;
                    user.FailedLoginCount = 0;
                }
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
                        TempData["ErrorToast"] = "Account number already exists.";
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

            TempData["SuccessToast"] = $"User details for {user.FullName} (@{user.Username}) updated successfully.";
            return RedirectToAction("Users");
        }
    }
}