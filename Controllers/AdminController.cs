using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartBank.Data;
using SmartBank.DTOs.Loans;
using SmartBank.Entities;
using SmartBank.Helpers;
using SmartBank.Security;
using SmartBank.Services.Interfaces;

namespace SmartBank.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly SmartBankDbContext _context;
        private readonly ILoanService _loanService;
        private readonly IWelcomeEmailService _welcomeEmailService;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            SmartBankDbContext context,
            ILoanService loanService,
            IWelcomeEmailService welcomeEmailService,
            ILogger<AdminController> logger)
        {
            _context = context;
            _loanService = loanService;
            _welcomeEmailService = welcomeEmailService;
            _logger = logger;
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
            else
            {
                // Default view: Exclude Rejected registration requests from active directory
                query = query.Where(u => u.Status != "Rejected");
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
                var accountPassword = PasswordGeneratorHelper.GenerateStrongPassword(10);
                var vaultPassword = PasswordGeneratorHelper.GenerateStrongPassword(10);
                while (vaultPassword == accountPassword)
                {
                    vaultPassword = PasswordGeneratorHelper.GenerateStrongPassword(10);
                }

                user.PasswordHash = SmartBank.Security.PasswordHasher.HashPassword(accountPassword);
                user.VaultPasswordHash = SmartBank.Security.PasswordHasher.HashPassword(vaultPassword);
                user.VaultPasswordSetAt = DateTime.UtcNow;
                user.IsFirstLogin = true;
                user.MustChangePasswordOnNextLogin = true;
                user.FailedVaultAttempts = 0;
                user.VaultLockedUntil = null;
                user.Status = "Active";
                user.TemporaryPasswordIssuedAtUtc = DateTime.UtcNow;
                user.UpdatedAt = DateTime.UtcNow;

                foreach (var acc in user.Accounts)
                {
                    acc.IsActive = true;
                    acc.UpdatedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();

                bool emailSent = await _welcomeEmailService.SendWelcomeEmailAsync(user.Email, user.FullName, user.Username, accountPassword, vaultPassword);

                if (emailSent)
                {
                    TempData["SuccessToast"] = $"User approved successfully! Dual credentials email (Account + Vault passwords) dispatched to {user.Email}.";
                }
                else
                {
                    _logger.LogWarning("[ADMIN APPROVAL] Failed to send welcome email to {Email}. Account Pass: {AccountPass}, Vault Pass: {VaultPass}", user.Email, accountPassword, vaultPassword);
                    TempData["ErrorToast"] = $"Email delivery failed. Manually provide — Account: {accountPassword} | Vault: {vaultPassword}";
                }
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

        // GET: Admin/DeleteUser (fallback for direct URL access)
        [HttpGet]
        public IActionResult DeleteUser()
        {
            return RedirectToAction("Users");
        }

        // POST: Admin/DeleteUser
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUser(int userId)
        {
            try
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
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                {
                    TempData["ErrorToast"] = "User not found.";
                    return RedirectToAction("Users");
                }

                var accountIds = user.Accounts.Select(a => a.Id).ToList();

                // 1. Delete TransferRequests referencing user or user's accounts (they have DeleteBehavior.Restrict)
                var relatedTransferRequests = await _context.TransferRequests
                    .Where(tr => tr.UserId == userId ||
                                 accountIds.Contains(tr.SourceAccountId) ||
                                 accountIds.Contains(tr.DestinationAccountId))
                    .ToListAsync();
                if (relatedTransferRequests.Any())
                {
                    _context.TransferRequests.RemoveRange(relatedTransferRequests);
                }

                // 2. Delete OtpVerifications (by UserId or Email)
                var cleanEmail = user.Email.Trim().ToLower();
                var otps = await _context.OtpVerifications
                    .Where(o => o.UserId == userId || o.Email.ToLower() == cleanEmail)
                    .ToListAsync();
                if (otps.Any())
                {
                    _context.OtpVerifications.RemoveRange(otps);
                }

                // 3. Remove User (EF Core & DB Cascade handles Accounts, RefreshTokens, ExternalLogins, OtpChallenges, LoanApplications, Transactions)
                _context.Users.Remove(user);

                await _context.SaveChangesAsync();

                TempData["SuccessToast"] = $"User profile and account for {user.FullName} (@{user.Username}) have been permanently deleted.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while deleting user {UserId}", userId);
                TempData["ErrorToast"] = $"Failed to delete user: {ex.Message}";
            }

            return RedirectToAction("Users");
        }

        // POST: Admin/SuspendUser
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SuspendUser(int userId, string suspensionType)
        {
            var user = await _context.Users.Include(u => u.Accounts).FirstOrDefaultAsync(u => u.Id == userId);
            if (user != null)
            {
                if (string.Equals(user.Status, "Suspended", StringComparison.OrdinalIgnoreCase))
                {
                    user.Status = "Active";
                    user.LockedUntil = null;
                    user.FailedLoginCount = 0;
                    user.UpdatedAt = DateTime.UtcNow;

                    foreach (var acc in user.Accounts)
                    {
                        acc.IsActive = true;
                        acc.UpdatedAt = DateTime.UtcNow;
                    }

                    TempData["SuccessToast"] = $"User {user.FullName} (@{user.Username}) has been Reactivated & Unlocked.";
                }
                else
                {
                    user.Status = "Suspended";
                    if (suspensionType == "1Hour") user.LockedUntil = DateTime.UtcNow.AddHours(1);
                    else if (suspensionType == "24Hours") user.LockedUntil = DateTime.UtcNow.AddHours(24);
                    else if (suspensionType == "7Days") user.LockedUntil = DateTime.UtcNow.AddDays(7);
                    else user.LockedUntil = DateTime.UtcNow.AddYears(100);

                    user.UpdatedAt = DateTime.UtcNow;

                    foreach (var acc in user.Accounts)
                    {
                        acc.IsActive = false;
                        acc.UpdatedAt = DateTime.UtcNow;
                    }

                    TempData["SuccessToast"] = $"User {user.FullName} (@{user.Username}) has been SUSPENDED ({suspensionType}). Login access revoked.";
                }

                await _context.SaveChangesAsync();
            }
            else
            {
                TempData["ErrorToast"] = "User not found.";
            }

            return RedirectToAction("Users");
        }

        // GET: Admin/GetUserReport
        [HttpGet]
        public async Task<IActionResult> GetUserReport(int userId)
        {
            var user = await _context.Users
                .Include(u => u.Accounts)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }

            var account = user.Accounts.FirstOrDefault();
            List<object> userTransactions = new();

            if (account != null)
            {
                var transactions = await _context.Transactions
                    .Where(t => t.AccountId == account.Id)
                    .OrderByDescending(t => t.Timestamp)
                    .ToListAsync();

                userTransactions = transactions.Select(t => (object)new
                {
                    t.Id,
                    TransactionId = $"TXN-{t.Id:D6}",
                    TransactionType = t.Type.ToString(),
                    t.Amount,
                    BalanceAfterTransaction = account.Balance,
                    Description = t.Type == TransactionType.Deposit ? "Account Cash Deposit" :
                                  t.Type == TransactionType.Withdraw ? "Account Cash Withdrawal" :
                                  t.Type == TransactionType.TransferOut ? $"Transfer to Account #{t.RelatedAccountId}" :
                                  $"Transfer from Account #{t.RelatedAccountId}",
                    Reference = t.RelatedAccountId.HasValue ? $"ACC-{t.RelatedAccountId}" : "Direct Bank Operation",
                    Status = "Completed",
                    Timestamp = t.Timestamp.ToString("MMM dd, yyyy hh:mm:ss tt")
                }).ToList();
            }

            var isLocked = user.LockedUntil.HasValue && user.LockedUntil.Value > DateTime.UtcNow;

            return Ok(new
            {
                userId = user.Id,
                fullName = user.FullName,
                username = user.Username,
                email = user.Email,
                phoneNumber = user.PhoneNumber ?? "Not Provided",
                nidNumber = user.NidNumber ?? "Not Provided",
                role = user.Role,
                status = user.Status,
                isLocked,
                lockedUntil = user.LockedUntil?.ToString("MMM dd, yyyy hh:mm tt") ?? null,
                accountOpeningDate = user.CreatedAt.ToString("MMM dd, yyyy hh:mm tt") + " UTC",
                createdAtRaw = user.CreatedAt,
                accountNumber = account?.AccountNumber ?? "N/A",
                accountBalance = account?.Balance ?? 0m,
                accountIsActive = account?.IsActive ?? false,
                transactions = userTransactions
            });
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

        // GET: Admin/GetUserDetails?userId=123
        [HttpGet]
        public async Task<IActionResult> GetUserDetails(int userId)
        {
            var user = await _context.Users
                .Include(u => u.Accounts)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                return Json(new { success = false, message = "User not found." });
            }

            var account = user.Accounts.FirstOrDefault();
            var txList = new List<object>();

            if (account != null)
            {
                var transactions = await _context.Transactions
                    .Where(t => t.AccountId == account.Id)
                    .OrderByDescending(t => t.Timestamp)
                    .ToListAsync();

                txList = transactions.Select(t => (object)new
                {
                    id = t.Id,
                    type = t.Type.ToString(),
                    amount = t.Amount,
                    timestamp = t.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                    relatedAccountId = t.RelatedAccountId,
                    isCredit = t.Type == TransactionType.Deposit || t.Type == TransactionType.TransferIn
                }).ToList();
            }

            return Json(new
            {
                success = true,
                user = new
                {
                    id = user.Id,
                    fullName = user.FullName,
                    username = user.Username,
                    email = user.Email,
                    phoneNumber = user.PhoneNumber ?? "Not provided",
                    nidNumber = user.NidNumber ?? "Not provided",
                    role = user.Role,
                    status = user.Status,
                    createdAt = user.CreatedAt.ToString("MMM dd, yyyy hh:mm tt") + " UTC",
                    accountNumber = account?.AccountNumber ?? "No Account Provisioned",
                    accountId = account?.Id ?? 0,
                    balance = account?.Balance ?? 0m,
                    isActive = account?.IsActive ?? false
                },
                transactions = txList
            });
        }

        // POST: Admin/AdminDepositFunds
        [HttpPost]
        public async Task<IActionResult> AdminDepositFunds([FromBody] AdminDepositDto dto)
        {
            if (dto == null || dto.UserId <= 0 || dto.Amount <= 0)
            {
                return BadRequest(new { message = "Invalid deposit parameters. Amount must be greater than zero." });
            }

            var user = await _context.Users
                .Include(u => u.Accounts)
                .FirstOrDefaultAsync(u => u.Id == dto.UserId);

            if (user == null)
            {
                return NotFound(new { message = "User record not found." });
            }

            var account = user.Accounts.FirstOrDefault();
            if (account == null)
            {
                return BadRequest(new { message = "No active banking account found for this user." });
            }

            using var dbTx = await _context.Database.BeginTransactionAsync();
            try
            {
                account.Balance += dto.Amount;
                account.UpdatedAt = DateTime.UtcNow;

                var txn = new Transaction
                {
                    AccountId = account.Id,
                    Type = TransactionType.Deposit,
                    Amount = dto.Amount,
                    Timestamp = DateTime.UtcNow,
                    RelatedAccountId = null
                };

                _context.Transactions.Add(txn);
                await _context.SaveChangesAsync();
                await dbTx.CommitAsync();

                _logger.LogInformation("[ADMIN DEPOSIT SUCCESS] Admin deposited ৳{Amount:N2} to Account {AccountNum} (User #{UserId})", dto.Amount, account.AccountNumber, user.Id);

                return Ok(new
                {
                    success = true,
                    message = $"Successfully deposited ৳{dto.Amount:N2} to account {account.AccountNumber}.",
                    newBalance = account.Balance
                });
            }
            catch (Exception ex)
            {
                await dbTx.RollbackAsync();
                _logger.LogError(ex, "Failed to execute Admin deposit for user {UserId}", dto.UserId);
                return StatusCode(500, new { message = "Internal error executing deposit: " + ex.Message });
            }
        }

        public class AdminDepositDto
        {
            public int UserId { get; set; }
            public decimal Amount { get; set; }
            public string? Reference { get; set; }
        }

        // GET: Admin/Loans
        [HttpGet]
        public async Task<IActionResult> Loans(string? statusFilter, string? search)
        {
            var apps = await _loanService.GetAllApplicationsForAdminAsync(statusFilter);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                apps = apps.Where(a => a.ApplicationNumber.ToLower().Contains(s)
                                    || a.CustomerName.ToLower().Contains(s)
                                    || a.CustomerEmail.ToLower().Contains(s)
                                    || a.AccountNumber.ToLower().Contains(s)).ToList();
            }

            var stats = await _loanService.GetAdminLoanStatsAsync();
            ViewBag.Stats = stats;
            ViewBag.CurrentStatus = statusFilter;
            ViewBag.CurrentSearch = search;

            return View(apps);
        }

        // POST: Admin/ApproveLoan
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveLoan(
            string applicationNumber, 
            decimal? approvedAmount, 
            decimal? interestRate, 
            int? tenureMonths, 
            string? comment)
        {
            var adminUsername = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? User.Identity?.Name ?? "Administrator";
            
            var app = await _loanService.GetApplicationByNumberAsync(0, applicationNumber, isAdmin: true);
            if (app == null)
            {
                TempData["ErrorToast"] = "Loan application not found.";
                return RedirectToAction("Loans");
            }

            var dto = new ApproveLoanDto
            {
                ApprovedAmount = approvedAmount ?? app.RequestedAmount,
                InterestRate = interestRate ?? app.IndicativeRate ?? 10.50m,
                TenureMonths = tenureMonths ?? app.RequestedTenureMonths ?? 12,
                AdminNote = comment ?? "Approved after banking underwriting review."
            };

            var (status, response) = await _loanService.ApproveLoanAsync(app.Id, dto, adminUsername);

            if (status == 200)
            {
                TempData["SuccessToast"] = $"Loan {applicationNumber} successfully approved! ৳{dto.ApprovedAmount:N2} disbursed and repayment schedule activated.";
            }
            else
            {
                TempData["ErrorToast"] = response.Message ?? "Failed to approve loan application.";
            }

            return RedirectToAction("Loans");
        }

        // POST: Admin/RejectLoan
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectLoan(string applicationNumber, string? comment)
        {
            var adminUsername = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? User.Identity?.Name ?? "Administrator";
            var app = await _loanService.GetApplicationByNumberAsync(0, applicationNumber, isAdmin: true);
            if (app == null)
            {
                TempData["ErrorToast"] = "Loan application not found.";
                return RedirectToAction("Loans");
            }

            var (status, response) = await _loanService.RejectLoanAsync(app.Id, comment ?? "Application rejected per risk guidelines.", adminUsername);

            if (status == 200)
            {
                TempData["InfoToast"] = $"Loan Application {applicationNumber} has been rejected.";
            }
            else
            {
                TempData["ErrorToast"] = response.Message ?? "Failed to reject loan application.";
            }

            return RedirectToAction("Loans");
        }

        // GET: Admin/LoanSchedule/{applicationId}
        [HttpGet]
        public async Task<IActionResult> LoanSchedule(int id)
        {
            var installments = await _loanService.GetInstallmentsAsync(id);
            var apps = await _loanService.GetAllApplicationsForAdminAsync();
            var app = apps.FirstOrDefault(a => a.Id == id);

            if (app == null)
            {
                TempData["ErrorToast"] = "Loan application not found.";
                return RedirectToAction("Loans");
            }

            ViewBag.LoanApplication = app;
            return View(installments);
        }

        // GET: Admin/LoanPayments
        [HttpGet]
        public async Task<IActionResult> LoanPayments()
        {
            var payments = await _loanService.GetAllPaymentsForAdminAsync();
            return View(payments);
        }

        // POST: Admin/RecordPayment
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecordPayment(RecordPaymentDto dto)
        {
            var adminUsername = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? User.Identity?.Name ?? "Administrator";
            var (status, response) = await _loanService.RecordPaymentAsync(dto, adminUsername);

            if (status == 200)
            {
                TempData["SuccessToast"] = $"Payment of ৳{dto.Amount:N2} recorded successfully.";
            }
            else
            {
                TempData["ErrorToast"] = response.Message ?? "Failed to record payment.";
            }

            return RedirectToAction("Loans");
        }

        // POST: Admin/MarkOverdue
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkOverdue()
        {
            var count = await _loanService.MarkOverdueInstallmentsAsync();
            TempData["InfoToast"] = count > 0 
                ? $"Overdue scan completed: {count} installment(s) marked overdue and late fees applied." 
                : "Overdue scan completed: No pending past-due installments found.";

            return RedirectToAction("Loans");
        }
    }
}