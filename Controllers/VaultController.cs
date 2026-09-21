using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartBank.Data;
using SmartBank.Entities;
using SmartBank.Security;
using SmartBank.Services.Interfaces;

namespace SmartBank.Controllers
{
    [Authorize]
    public class VaultController : Controller
    {
        private readonly SmartBankDbContext _context;
        private readonly IOtpService _otpService;
        private readonly IEmailService _emailService;
        private readonly IAuthService _authService;
        private readonly ILogger<VaultController> _logger;

        private const string VaultSessionKey = "VaultUnlockedTimestamp";
        private const string VaultUserKey = "VaultUnlockedUserId";
        private const int VaultTimeoutMinutes = 5;

        public VaultController(
            SmartBankDbContext context,
            IOtpService otpService,
            IEmailService emailService,
            IAuthService authService,
            ILogger<VaultController> logger)
        {
            _context = context;
            _otpService = otpService;
            _emailService = emailService;
            _authService = authService;
            _logger = logger;
        }

        private int GetCurrentUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
            return int.TryParse(claim, out var id) ? id : 0;
        }

        private bool IsVaultSessionActive(int userId)
        {
            var sessionUserId = HttpContext.Session.GetString(VaultUserKey);
            var sessionTimestampStr = HttpContext.Session.GetString(VaultSessionKey);

            if (string.IsNullOrEmpty(sessionUserId) || string.IsNullOrEmpty(sessionTimestampStr))
            {
                return false;
            }

            if (sessionUserId != userId.ToString())
            {
                return false;
            }

            if (DateTime.TryParse(sessionTimestampStr, out var unlockedAt))
            {
                if (DateTime.UtcNow - unlockedAt <= TimeSpan.FromMinutes(VaultTimeoutMinutes))
                {
                    // Refresh sliding session
                    HttpContext.Session.SetString(VaultSessionKey, DateTime.UtcNow.ToString("O"));
                    return true;
                }
            }

            // Expired
            HttpContext.Session.Remove(VaultSessionKey);
            HttpContext.Session.Remove(VaultUserKey);
            return false;
        }

        private void SetVaultSessionActive(int userId)
        {
            HttpContext.Session.SetString(VaultUserKey, userId.ToString());
            HttpContext.Session.SetString(VaultSessionKey, DateTime.UtcNow.ToString("O"));
        }

        private void ClearVaultSession()
        {
            HttpContext.Session.Remove(VaultSessionKey);
            HttpContext.Session.Remove(VaultUserKey);
        }

        // GET: /Vault
        [HttpGet]
        public async Task<IActionResult> Index(string? typeFilter, string? search)
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return RedirectToAction("Login", "Account");

            var user = await _context.Users
                .Include(u => u.Accounts)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null) return RedirectToAction("Login", "Account");

            if (user.IsFirstLogin || user.MustChangePasswordOnNextLogin)
            {
                return RedirectToAction("FirstLoginPasswordChange", "Account");
            }

            var isUnlocked = IsVaultSessionActive(userId);
            var isLockedOut = user.VaultLockedUntil.HasValue && user.VaultLockedUntil.Value > DateTime.UtcNow;
            int remainingLockoutSeconds = 0;
            if (isLockedOut)
            {
                remainingLockoutSeconds = (int)Math.Ceiling((user.VaultLockedUntil!.Value - DateTime.UtcNow).TotalSeconds);
                if (remainingLockoutSeconds < 0) remainingLockoutSeconds = 0;
            }

            ViewBag.IsUnlocked = isUnlocked;
            ViewBag.IsLockedOut = isLockedOut;
            ViewBag.RemainingLockoutSeconds = remainingLockoutSeconds;
            ViewBag.FailedAttempts = user.FailedVaultAttempts;
            ViewBag.User = user;

            if (!isUnlocked)
            {
                return View();
            }

            // Loaded unlocked financial data
            var account = user.Accounts.FirstOrDefault();
            var allTransactions = new List<Transaction>();
            decimal totalInflow = 0;
            decimal totalOutflow = 0;

            if (account != null)
            {
                var query = _context.Transactions
                    .Where(t => t.AccountId == account.Id)
                    .OrderByDescending(t => t.Timestamp)
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(typeFilter))
                {
                    if (Enum.TryParse<TransactionType>(typeFilter, true, out var parsedType))
                    {
                        query = query.Where(t => t.Type == parsedType);
                    }
                }

                allTransactions = await query.ToListAsync();

                var fullTxList = await _context.Transactions
                    .Where(t => t.AccountId == account.Id)
                    .ToListAsync();

                totalInflow = fullTxList
                    .Where(t => t.Type == TransactionType.Deposit || t.Type == TransactionType.TransferIn)
                    .Sum(t => t.Amount);

                totalOutflow = fullTxList
                    .Where(t => t.Type == TransactionType.Withdraw || t.Type == TransactionType.TransferOut)
                    .Sum(t => t.Amount);
            }

            ViewBag.Account = account;
            ViewBag.Transactions = allTransactions;
            ViewBag.TotalInflow = totalInflow;
            ViewBag.TotalOutflow = totalOutflow;
            ViewBag.SelectedType = typeFilter;
            ViewBag.Search = search;
            ViewBag.SessionTimeoutSeconds = VaultTimeoutMinutes * 60;

            return View();
        }

        // POST: /Vault/Unlock
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Unlock(string vaultPassword)
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return RedirectToAction("Login", "Account");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return RedirectToAction("Login", "Account");

            // 1. Check Rate Limit / Lockout
            if (user.VaultLockedUntil.HasValue && user.VaultLockedUntil.Value > DateTime.UtcNow)
            {
                var remainingMinutes = (int)Math.Ceiling((user.VaultLockedUntil.Value - DateTime.UtcNow).TotalMinutes);
                TempData["VaultError"] = $"Vault access is temporarily locked due to repeated failed attempts. Please retry in {remainingMinutes} minute(s).";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(vaultPassword))
            {
                TempData["VaultError"] = "Please enter your Security Vault Password.";
                return RedirectToAction(nameof(Index));
            }

            // 2. Check Vault Password
            var isValid = !string.IsNullOrEmpty(user.VaultPasswordHash) &&
                          PasswordHasher.VerifyPassword(vaultPassword, user.VaultPasswordHash);

            if (!isValid)
            {
                user.FailedVaultAttempts++;
                user.UpdatedAt = DateTime.UtcNow;

                if (user.FailedVaultAttempts >= 3)
                {
                    user.VaultLockedUntil = DateTime.UtcNow.AddMinutes(5);
                    await _context.SaveChangesAsync();
                    _logger.LogWarning("[VAULT LOCKOUT] User {UserId} (@{Username}) reached 3 failed vault attempts. Locked for 5 minutes.", user.Id, user.Username);
                    TempData["VaultError"] = "Too many failed attempts. Vault access is locked for 5 minutes.";
                    return RedirectToAction(nameof(Index));
                }

                await _context.SaveChangesAsync();
                var attemptsLeft = 3 - user.FailedVaultAttempts;
                TempData["VaultError"] = $"Incorrect Vault Password. {attemptsLeft} attempt(s) remaining before a 5-minute cooldown.";
                return RedirectToAction(nameof(Index));
            }

            // 3. Success: Reset attempts & activate session
            user.FailedVaultAttempts = 0;
            user.VaultLockedUntil = null;
            user.LastVaultUnlockAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            SetVaultSessionActive(userId);
            _logger.LogInformation("[VAULT UNLOCKED] User {UserId} successfully unlocked vault.", user.Id);

            TempData["SuccessToast"] = "Vault unlocked successfully. Session active for 5 minutes.";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Vault/Lock
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Lock()
        {
            ClearVaultSession();
            TempData["InfoToast"] = "Vault session locked securely.";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Vault/ChangeAccountPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeAccountPassword(string currentPassword, string newPassword, string confirmPassword)
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return RedirectToAction("Login", "Account");

            if (!IsVaultSessionActive(userId))
            {
                TempData["ErrorToast"] = "Vault session has expired. Please unlock the vault to perform security actions.";
                return RedirectToAction(nameof(Index));
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return RedirectToAction("Login", "Account");

            if (!PasswordHasher.VerifyPassword(currentPassword, user.PasswordHash))
            {
                TempData["ErrorToast"] = "Incorrect current Account Password.";
                return RedirectToAction(nameof(Index));
            }

            if (newPassword != confirmPassword)
            {
                TempData["ErrorToast"] = "New Account Password and confirmation do not match.";
                return RedirectToAction(nameof(Index));
            }

            var (isValid, errorMessage) = PasswordValidator.Validate(newPassword);
            if (!isValid)
            {
                TempData["ErrorToast"] = errorMessage;
                return RedirectToAction(nameof(Index));
            }

            user.PasswordHash = PasswordHasher.HashPassword(newPassword.Trim());
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("[SECURITY] User {UserId} changed Account Password from Vault.", user.Id);
            TempData["SuccessToast"] = "Account Login Password updated successfully!";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Vault/ChangeVaultPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeVaultPassword(string currentVaultPassword, string newVaultPassword, string confirmVaultPassword)
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return RedirectToAction("Login", "Account");

            if (!IsVaultSessionActive(userId))
            {
                TempData["ErrorToast"] = "Vault session has expired. Please unlock the vault to perform security actions.";
                return RedirectToAction(nameof(Index));
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return RedirectToAction("Login", "Account");

            if (!string.IsNullOrEmpty(user.VaultPasswordHash) && !PasswordHasher.VerifyPassword(currentVaultPassword, user.VaultPasswordHash))
            {
                TempData["ErrorToast"] = "Incorrect current Security Vault Password.";
                return RedirectToAction(nameof(Index));
            }

            if (newVaultPassword != confirmVaultPassword)
            {
                TempData["ErrorToast"] = "New Vault Password and confirmation do not match.";
                return RedirectToAction(nameof(Index));
            }

            var (isValid, errorMessage) = PasswordValidator.Validate(newVaultPassword);
            if (!isValid)
            {
                TempData["ErrorToast"] = errorMessage;
                return RedirectToAction(nameof(Index));
            }

            // Ensure vault password differs from current account password
            if (PasswordHasher.VerifyPassword(newVaultPassword.Trim(), user.PasswordHash))
            {
                TempData["ErrorToast"] = "Security Vault Password must be different from your Account Login Password.";
                return RedirectToAction(nameof(Index));
            }

            user.VaultPasswordHash = PasswordHasher.HashPassword(newVaultPassword.Trim());
            user.VaultPasswordSetAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("[SECURITY] User {UserId} changed Vault Password from Vault.", user.Id);
            TempData["SuccessToast"] = "Security Vault Password updated successfully!";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Vault/ForgotVaultPasswordRequest
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotVaultPasswordRequest(string accountPassword)
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return Json(new { success = false, message = "Not authenticated." });

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return Json(new { success = false, message = "User not found." });

            if (string.IsNullOrWhiteSpace(accountPassword) || !PasswordHasher.VerifyPassword(accountPassword, user.PasswordHash))
            {
                return Json(new { success = false, message = "Incorrect Account Login Password. Cannot verify identity." });
            }

            var rawOtp = _otpService.GenerateCode(6);
            var salt = Guid.NewGuid().ToString("N");
            var codeHash = _otpService.Hash(rawOtp, salt);

            var cleanEmail = user.Email.Trim().ToLowerInvariant();
            var otpRecord = new OtpVerification
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Email = cleanEmail,
                CodeHash = codeHash,
                Salt = salt,
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
                AttemptCount = 0,
                IsUsed = false,
                Purpose = OtpPurpose.Login
            };

            _context.OtpVerifications.Add(otpRecord);
            await _context.SaveChangesAsync();

            await _emailService.SendOtpAsync(cleanEmail, rawOtp);

            HttpContext.Session.SetString("VaultResetPendingEmail", cleanEmail);

            return Json(new { success = true, message = "A 6-digit verification code has been dispatched to your email." });
        }

        // POST: /Vault/ResetVaultPasswordWithOtp
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetVaultPasswordWithOtp(string otpCode, string newVaultPassword, string confirmVaultPassword)
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return Json(new { success = false, message = "Not authenticated." });

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return Json(new { success = false, message = "User not found." });

            if (string.IsNullOrWhiteSpace(otpCode) || otpCode.Length != 6)
            {
                return Json(new { success = false, message = "Please enter a valid 6-digit verification code." });
            }

            var cleanEmail = user.Email.Trim().ToLowerInvariant();
            var otpRecord = await _context.OtpVerifications
                .Where(o => o.Email == cleanEmail && !o.IsUsed)
                .OrderByDescending(o => o.CreatedAtUtc)
                .FirstOrDefaultAsync();

            if (otpRecord == null || otpRecord.ExpiresAtUtc < DateTime.UtcNow)
            {
                return Json(new { success = false, message = "Verification code has expired. Please request a new one." });
            }

            if (!_otpService.Verify(otpCode.Trim(), otpRecord.CodeHash, otpRecord.Salt))
            {
                otpRecord.AttemptCount++;
                await _context.SaveChangesAsync();
                return Json(new { success = false, message = "Invalid verification code." });
            }

            otpRecord.IsUsed = true;

            if (newVaultPassword != confirmVaultPassword)
            {
                return Json(new { success = false, message = "New Vault Password and confirmation do not match." });
            }

            var (isValid, errorMessage) = PasswordValidator.Validate(newVaultPassword);
            if (!isValid)
            {
                return Json(new { success = false, message = errorMessage });
            }

            if (PasswordHasher.VerifyPassword(newVaultPassword.Trim(), user.PasswordHash))
            {
                return Json(new { success = false, message = "Security Vault Password must be different from your Account Login Password." });
            }

            user.VaultPasswordHash = PasswordHasher.HashPassword(newVaultPassword.Trim());
            user.VaultPasswordSetAt = DateTime.UtcNow;
            user.FailedVaultAttempts = 0;
            user.VaultLockedUntil = null;
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            SetVaultSessionActive(userId);
            _logger.LogInformation("[SECURITY] User {UserId} reset Vault Password via OTP.", user.Id);

            return Json(new { success = true, message = "Vault Password has been reset successfully. Your vault is now unlocked!" });
        }

        // POST: /Vault/UpdateProfile
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfile(string fullName, string email, string? phoneNumber)
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return RedirectToAction("Login", "Account");

            if (!IsVaultSessionActive(userId))
            {
                TempData["ErrorToast"] = "Vault session has expired. Please unlock the vault to update your profile.";
                return RedirectToAction(nameof(Index));
            }

            var req = new DTOs.Auth.UpdateProfileRequest
            {
                FullName = fullName?.Trim() ?? string.Empty,
                Email = email?.Trim() ?? string.Empty,
                PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim()
            };

            var (status, response) = await _authService.UpdateProfileAsync(userId, req);

            if (status == 200)
            {
                TempData["SuccessToast"] = "Your profile information has been updated successfully.";
            }
            else
            {
                TempData["ErrorToast"] = response.Message ?? "Failed to update profile details.";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
