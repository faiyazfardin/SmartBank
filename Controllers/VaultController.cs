using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartBank.Data;
using SmartBank.Entities;
using SmartBank.Security;
using SmartBank.Services.Interfaces;
using SmartBank.ViewModels;

namespace SmartBank.Controllers
{
    [Authorize]
    public class VaultController : Controller
    {
        private static readonly ConcurrentDictionary<string, (int UserId, DateTime Expiration)> _vaultResetTokens = new();

        private readonly SmartBankDbContext _context;
        private readonly IOtpService _otpService;
        private readonly IEmailService _emailService;
        private readonly IAuthService _authService;
        private readonly IRiskService _riskService;
        private readonly ILogger<VaultController> _logger;

        // PART B: Auto-lock after 1 MINUTE of inactivity
        private const int VaultTimeoutMinutes = 1;

        public VaultController(
            SmartBankDbContext context,
            IOtpService otpService,
            IEmailService emailService,
            IAuthService authService,
            IRiskService riskService,
            ILogger<VaultController> logger)
        {
            _context = context;
            _otpService = otpService;
            _emailService = emailService;
            _authService = authService;
            _riskService = riskService;
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
            // PART A: Multiversal lock state rule
            var trustRestored = HttpContext.Session.GetString("VaultTrustRestored");
            if (trustRestored == "false")
            {
                return false;
            }

            var sessionUserId = HttpContext.Session.GetString("VaultUnlockedUserId");
            var sessionTimestampStr = HttpContext.Session.GetString("VaultUnlockedTimestamp");

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
                    // Sliding expiration
                    HttpContext.Session.SetString("VaultUnlockedTimestamp", DateTime.UtcNow.ToString("O"));
                    return true;
                }
            }

            ClearVaultSession();
            return false;
        }

        private void SetVaultSessionActive(int userId)
        {
            HttpContext.Session.SetString("VaultUnlockedUserId", userId.ToString());
            HttpContext.Session.SetString("VaultUnlockedTimestamp", DateTime.UtcNow.ToString("O"));
        }

        private void ClearVaultSession()
        {
            HttpContext.Session.Remove("VaultUnlockedTimestamp");
            HttpContext.Session.Remove("VaultUnlockedUserId");
        }

        private static string MaskEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email) || !email.Contains("@"))
                return email;

            var parts = email.Split('@');
            var username = parts[0];
            var domainParts = parts[1].Split('.');

            string maskedUsername;
            if (username.Length <= 2)
            {
                maskedUsername = username[0] + "*";
            }
            else
            {
                maskedUsername = username[0] + new string('*', username.Length - 2) + username[^1];
            }

            string maskedDomainName;
            var domainName = domainParts[0];
            if (domainName.Length <= 2)
            {
                maskedDomainName = domainName[0] + "*";
            }
            else
            {
                maskedDomainName = domainName[0] + new string('*', domainName.Length - 2) + domainName[^1];
            }

            var extension = string.Join(".", domainParts.Skip(1));
            return $"{maskedUsername}@{maskedDomainName}.{extension}";
        }

        // GET: /Vault
        [HttpGet]
        public async Task<IActionResult> Index(string? typeFilter, string? search)
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return RedirectToAction("Login", "Account");

            var user = await _context.Users
                .Include(u => u.Accounts)
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null) return RedirectToAction("Login", "Account");

            if (user.IsFirstLogin || user.MustChangePasswordOnNextLogin)
            {
                return RedirectToAction("FirstLoginPasswordChange", "Account");
            }

            // PART F: Forced Security Question Setup check for existing users missing security questions
            if (string.IsNullOrWhiteSpace(user.SecurityQuestion) || string.IsNullOrWhiteSpace(user.SecurityAnswerHash))
            {
                return RedirectToAction("ForceSecurityQuestionSetup", "Account");
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

            var account = user.Accounts.FirstOrDefault();
            var allTransactions = new List<Transaction>();
            decimal totalInflow = 0;
            decimal totalOutflow = 0;

            if (account != null)
            {
                var query = _context.Transactions
                    .AsNoTracking()
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
                    .AsNoTracking()
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

            if (string.Equals(user.Status, "Suspended", StringComparison.OrdinalIgnoreCase))
            {
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                TempData["ErrorToast"] = "Your account has been frozen. Access is restricted.";
                return RedirectToAction("Login", "Account");
            }

            if (string.IsNullOrWhiteSpace(vaultPassword))
            {
                TempData["VaultError"] = "Please enter your Security Vault Password.";
                return RedirectToAction(nameof(Index));
            }

            // Verify Vault Password
            var isValid = !string.IsNullOrEmpty(user.VaultPasswordHash) &&
                          PasswordHasher.VerifyPassword(vaultPassword, user.VaultPasswordHash);

            if (!isValid)
            {
                user.FailedVaultAttempts++;
                user.UpdatedAt = DateTime.UtcNow;

                // SCORING TRIGGER: Wrong vault password +8 per attempt
                await _riskService.RecordRiskEventAsync(user.Id, "WrongVaultPassword", 8, "Wrong vault password attempt");

                // PART B: 3 failed vault attempts trigger ACCOUNT FREEZE & bonus penalty (+15 bonus)
                if (user.FailedVaultAttempts >= 3)
                {
                    await _riskService.RecordRiskEventAsync(user.Id, "Vault3FailedBonus", 15, "3 consecutive failed vault password attempts bonus");
                    user.Status = "Suspended";
                    user.LockedUntil = DateTime.UtcNow.AddYears(100);
                    foreach (var acc in user.Accounts) { acc.IsActive = false; }
                    await _context.SaveChangesAsync();
                    _logger.LogWarning("[SECURITY AUDIT - ACCOUNT FREEZE] User {UserId} (@{Username}) reached 3 failed vault attempts. Account FROZEN.", user.Id, user.Username);

                    await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    TempData["ErrorToast"] = "Your account has been frozen due to 3 failed Vault password attempts. Please contact Administrator support.";
                    return RedirectToAction("Login", "Account");
                }

                await _context.SaveChangesAsync();
                var attemptsLeft = 3 - user.FailedVaultAttempts;
                TempData["VaultError"] = $"Incorrect Vault Password. {attemptsLeft} attempt(s) remaining before account freeze.";
                return RedirectToAction(nameof(Index));
            }

            // Success: Reset attempts & restore trust
            user.FailedVaultAttempts = 0;
            user.VaultLockedUntil = null;
            user.LastVaultUnlockAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            HttpContext.Session.SetString("VaultTrustRestored", "true");
            HttpContext.Session.Remove("VaultLockReason");
            SetVaultSessionActive(userId);
            _logger.LogInformation("[VAULT UNLOCKED] User {UserId} successfully unlocked vault.", user.Id);

            TempData["SuccessToast"] = "Vault unlocked successfully. Session active.";
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

        // =========================================================================
        // PART C — VAULT PASSWORD RESET FLOW
        // =========================================================================

        // STEP 1: Security Question (GET)
        [HttpGet]
        public async Task<IActionResult> ForgotVaultPasswordStep1()
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return RedirectToAction("Login", "Account");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return RedirectToAction("Login", "Account");

            if (string.IsNullOrWhiteSpace(user.SecurityQuestion) || string.IsNullOrWhiteSpace(user.SecurityAnswerHash))
            {
                TempData["InfoToast"] = "Security question is not configured for your account. Please set it up now.";
                return RedirectToAction("ForceSecurityQuestionSetup", "Account");
            }

            // SCORING TRIGGER: Vault password reset request +10
            await _riskService.RecordRiskEventAsync(user.Id, "VaultPasswordResetRequest", 10, "Vault password reset initiated");

            var model = new VerifySecurityQuestionViewModel
            {
                SecurityQuestion = user.SecurityQuestion
            };

            return View(model);
        }

        // STEP 1: Security Question (POST Verification)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifySecurityQuestion(VerifySecurityQuestionViewModel model)
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return RedirectToAction("Login", "Account");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return RedirectToAction("Login", "Account");

            if (string.IsNullOrWhiteSpace(user.SecurityAnswerHash))
            {
                return RedirectToAction("ForceSecurityQuestionSetup", "Account");
            }

            model.SecurityQuestion = user.SecurityQuestion ?? "Security Question";

            if (string.IsNullOrWhiteSpace(model.SecurityAnswer))
            {
                ModelState.AddModelError(nameof(model.SecurityAnswer), "Please enter your security answer.");
                return View("ForgotVaultPasswordStep1", model);
            }

            // Comparison rules: CASE-INSENSITIVE + TRIMMED against hashed answer
            bool isAnswerCorrect = PasswordHasher.VerifyPassword(model.SecurityAnswer.Trim().ToLowerInvariant(), user.SecurityAnswerHash);

            if (!isAnswerCorrect)
            {
                int failures = (HttpContext.Session.GetInt32("SecurityQuestionFailures") ?? 0) + 1;
                HttpContext.Session.SetInt32("SecurityQuestionFailures", failures);

                // SCORING TRIGGER: Wrong security question answer +10 per attempt
                await _riskService.RecordRiskEventAsync(user.Id, "WrongSecurityQuestion", 10, $"Wrong security question attempt #{failures}");

                // FREEZE TRIGGER: Wrong answer 3 TIMES -> FREEZE THE ACCOUNT
                if (failures >= 3)
                {
                    user.Status = "Suspended";
                    user.LockedUntil = DateTime.UtcNow.AddYears(100);
                    user.UpdatedAt = DateTime.UtcNow;
                    foreach (var acc in user.Accounts) { acc.IsActive = false; }
                    await _context.SaveChangesAsync();

                    _logger.LogWarning("[SECURITY AUDIT - ACCOUNT FREEZE] Account frozen for User {UserId} due to 3 failed security question attempts.", user.Id);
                    await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                    TempData["ErrorToast"] = "Your account has been frozen due to 3 incorrect security question attempts. Please contact Administrator support.";
                    return RedirectToAction("Login", "Account");
                }

                int remaining = 3 - failures;
                ModelState.AddModelError(nameof(model.SecurityAnswer), $"Incorrect security answer. {remaining} attempt(s) remaining before account freeze.");
                return View("ForgotVaultPasswordStep1", model);
            }

            // Correct answer -> proceed to Step 2
            HttpContext.Session.Remove("SecurityQuestionFailures");
            HttpContext.Session.SetString("VaultReset_QuestionVerified", "true");

            // Dispatch OTP #1
            var rawOtp = _otpService.GenerateCode(6);
            var salt = Guid.NewGuid().ToString("N");
            var codeHash = _otpService.Hash(rawOtp, salt);
            var cleanEmail = user.Email.Trim().ToLowerInvariant();

            // Clear old pending VaultReset OTPs
            var oldOtps = await _context.OtpVerifications
                .Where(o => o.UserId == user.Id && o.Purpose == OtpPurpose.VaultReset && !o.IsUsed)
                .ToListAsync();
            foreach (var old in oldOtps)
            {
                old.IsUsed = true;
            }

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
                Purpose = OtpPurpose.VaultReset
            };

            _context.OtpVerifications.Add(otpRecord);
            await _context.SaveChangesAsync();

            await _emailService.SendOtpAsync(cleanEmail, rawOtp);

            HttpContext.Session.SetInt32("VaultReset_OtpCount", 1);

            _logger.LogInformation("[SECURITY AUDIT] Security question verified for User {UserId}. OTP #1 sent to {MaskedEmail}.", user.Id, MaskEmail(user.Email));
            TempData["SuccessToast"] = $"Security answer verified! Verification OTP #1 sent to {MaskEmail(user.Email)}.";

            return RedirectToAction(nameof(ForgotVaultPasswordOtp));
        }

        // STEP 2: OTP View (GET)
        [HttpGet]
        public async Task<IActionResult> ForgotVaultPasswordOtp()
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return RedirectToAction("Login", "Account");

            var verified = HttpContext.Session.GetString("VaultReset_QuestionVerified");
            if (verified != "true")
            {
                return RedirectToAction(nameof(ForgotVaultPasswordStep1));
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return RedirectToAction("Login", "Account");

            int otpCount = HttpContext.Session.GetInt32("VaultReset_OtpCount") ?? 1;
            ViewBag.MaskedEmail = MaskEmail(user.Email);
            ViewBag.OtpCount = otpCount;

            return View();
        }

        // STEP 2: Verify OTP (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyVaultResetOtp(string otpCode)
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return RedirectToAction("Login", "Account");

            var verified = HttpContext.Session.GetString("VaultReset_QuestionVerified");
            if (verified != "true")
            {
                return RedirectToAction(nameof(ForgotVaultPasswordStep1));
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return RedirectToAction("Login", "Account");

            int otpCount = HttpContext.Session.GetInt32("VaultReset_OtpCount") ?? 1;

            var otpRecord = await _context.OtpVerifications
                .Where(o => o.UserId == user.Id && o.Purpose == OtpPurpose.VaultReset && !o.IsUsed)
                .OrderByDescending(o => o.CreatedAtUtc)
                .FirstOrDefaultAsync();

            // FREEZE TRIGGER: If OTP expired and was OTP #3 (or max exhausted)
            if (otpRecord == null || otpRecord.ExpiresAtUtc < DateTime.UtcNow)
            {
                // SCORING TRIGGER: Wrong/expired OTP +5
                await _riskService.RecordRiskEventAsync(user.Id, "WrongOtp", 5, "Expired vault reset OTP");

                if (otpCount >= 3 || (otpRecord != null && otpCount == 3))
                {
                    // SCORING TRIGGER: All 3 OTPs exhausted +20
                    await _riskService.RecordRiskEventAsync(user.Id, "Otp3Exhausted", 20, "All 3 vault reset OTPs exhausted");

                    user.Status = "Suspended";
                    user.LockedUntil = DateTime.UtcNow.AddYears(100);
                    user.UpdatedAt = DateTime.UtcNow;
                    foreach (var acc in user.Accounts) { acc.IsActive = false; }
                    await _context.SaveChangesAsync();

                    _logger.LogWarning("[SECURITY AUDIT - ACCOUNT FREEZE] Account frozen for User {UserId} because all 3 vault reset OTPs expired without completion.", user.Id);
                    await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                    TempData["ErrorToast"] = "Your account has been frozen because all 3 verification codes expired without completing the vault reset. Please contact Administrator support.";
                    return RedirectToAction("Login", "Account");
                }

                ViewBag.Error = $"Verification code has expired. You may request OTP #{otpCount + 1}.";
                ViewBag.MaskedEmail = MaskEmail(user.Email);
                ViewBag.OtpCount = otpCount;
                return View("ForgotVaultPasswordOtp");
            }

            if (string.IsNullOrWhiteSpace(otpCode) || !_otpService.Verify(otpCode.Trim(), otpRecord.CodeHash, otpRecord.Salt))
            {
                if (otpRecord != null)
                {
                    otpRecord.AttemptCount++;
                    await _context.SaveChangesAsync();
                }

                // SCORING TRIGGER: Wrong OTP +5
                await _riskService.RecordRiskEventAsync(user.Id, "WrongOtp", 5, "Invalid vault reset OTP code");

                ViewBag.Error = "Invalid 6-digit verification code.";
                ViewBag.MaskedEmail = MaskEmail(user.Email);
                ViewBag.OtpCount = otpCount;
                return View("ForgotVaultPasswordOtp");
            }

            // OTP verified successfully
            otpRecord.IsUsed = true;
            await _context.SaveChangesAsync();

            var resetToken = Guid.NewGuid().ToString("N");
            _vaultResetTokens[resetToken] = (user.Id, DateTime.UtcNow.AddMinutes(15));

            HttpContext.Session.Remove("VaultReset_QuestionVerified");
            HttpContext.Session.Remove("VaultReset_OtpCount");

            _logger.LogInformation("[SECURITY AUDIT] OTP verified for User {UserId} vault reset.", user.Id);
            return RedirectToAction(nameof(ResetVaultPassword), new { token = resetToken });
        }

        // STEP 2: Request Next OTP (AJAX / POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestNextVaultOtp()
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return Json(new { success = false, message = "Not authenticated." });

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return Json(new { success = false, message = "User not found." });

            int currentCount = HttpContext.Session.GetInt32("VaultReset_OtpCount") ?? 1;
            if (currentCount >= 3)
            {
                return Json(new { success = false, message = "Maximum OTP limit reached (3 of 3). No further codes can be requested." });
            }

            int nextCount = currentCount + 1;
            HttpContext.Session.SetInt32("VaultReset_OtpCount", nextCount);

            // Invalidate prior pending OTPs
            var oldOtps = await _context.OtpVerifications
                .Where(o => o.UserId == user.Id && o.Purpose == OtpPurpose.VaultReset && !o.IsUsed)
                .ToListAsync();
            foreach (var old in oldOtps)
            {
                old.IsUsed = true;
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
                Purpose = OtpPurpose.VaultReset
            };

            _context.OtpVerifications.Add(otpRecord);
            await _context.SaveChangesAsync();

            await _emailService.SendOtpAsync(cleanEmail, rawOtp);
            _logger.LogInformation("[SECURITY AUDIT] OTP #{OtpCount} sent to User {UserId} for vault reset.", nextCount, user.Id);

            return Json(new { success = true, message = $"Verification OTP #{nextCount} of 3 sent to {MaskEmail(user.Email)}.", otpCount = nextCount });
        }

        // STEP 3: Set New Vault Password (GET)
        [HttpGet]
        public async Task<IActionResult> ResetVaultPassword(string token)
        {
            if (string.IsNullOrEmpty(token) || !_vaultResetTokens.TryGetValue(token, out var val) || val.Expiration < DateTime.UtcNow)
            {
                TempData["ErrorToast"] = "Vault reset session expired or invalid. Please restart recovery.";
                return RedirectToAction(nameof(ForgotVaultPasswordStep1));
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == val.UserId);
            if (user == null) return RedirectToAction("Login", "Account");

            var model = new ResetVaultPasswordViewModel
            {
                Token = token
            };

            return View(model);
        }

        // STEP 3: Set New Vault Password (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetVaultPassword(ResetVaultPasswordViewModel model)
        {
            if (string.IsNullOrEmpty(model.Token) || !_vaultResetTokens.TryGetValue(model.Token, out var val) || val.Expiration < DateTime.UtcNow)
            {
                TempData["ErrorToast"] = "Vault reset session expired. Please restart recovery.";
                return RedirectToAction(nameof(ForgotVaultPasswordStep1));
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == val.UserId);
            if (user == null) return RedirectToAction("Login", "Account");

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var (isValid, errorMessage) = PasswordValidator.Validate(model.NewVaultPassword);
            if (!isValid)
            {
                ModelState.AddModelError(nameof(model.NewVaultPassword), errorMessage);
                return View(model);
            }

            if (PasswordHasher.VerifyPassword(model.NewVaultPassword.Trim(), user.PasswordHash))
            {
                ModelState.AddModelError(nameof(model.NewVaultPassword), "Security Vault Password must be different from your Account Login Password.");
                return View(model);
            }

            user.VaultPasswordHash = PasswordHasher.HashPassword(model.NewVaultPassword.Trim());
            user.VaultPasswordSetAt = DateTime.UtcNow;
            user.FailedVaultAttempts = 0;
            user.VaultLockedUntil = null;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _vaultResetTokens.TryRemove(model.Token, out _);

            HttpContext.Session.SetString("VaultTrustRestored", "true");
            HttpContext.Session.Remove("VaultLockReason");
            SetVaultSessionActive(user.Id);

            _logger.LogInformation("[SECURITY AUDIT] Vault Password successfully reset for User {UserId}.", user.Id);
            TempData["SuccessToast"] = "Vault Password has been reset successfully! Your vault is now unlocked.";

            return RedirectToAction(nameof(Index));
        }

        // =========================================================================
        // PART D — CHANGE SECURITY QUESTION FROM VAULT SETTINGS
        // =========================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeSecurityQuestion(ChangeSecurityQuestionViewModel model)
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return RedirectToAction("Login", "Account");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return RedirectToAction("Login", "Account");

            if (!ModelState.IsValid)
            {
                TempData["ErrorToast"] = "Please fill in all required security question fields.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrEmpty(user.VaultPasswordHash) || !PasswordHasher.VerifyPassword(model.CurrentVaultPassword, user.VaultPasswordHash))
            {
                TempData["ErrorToast"] = "Incorrect Current Vault Password. Access denied.";
                return RedirectToAction(nameof(Index));
            }

            string question = model.SecurityQuestion == "Other" && !string.IsNullOrWhiteSpace(model.CustomSecurityQuestion)
                ? model.CustomSecurityQuestion.Trim()
                : model.SecurityQuestion.Trim();

            user.SecurityQuestion = question;
            user.SecurityAnswerHash = PasswordHasher.HashPassword(model.SecurityAnswer.Trim().ToLowerInvariant());
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            _logger.LogInformation("[SECURITY AUDIT] User {UserId} updated Vault security question.", user.Id);

            TempData["SuccessToast"] = "Security question and secret answer updated successfully!";
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

            _logger.LogInformation("[SECURITY AUDIT] User {UserId} changed Account Password from Vault.", user.Id);
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

            if (PasswordHasher.VerifyPassword(newVaultPassword.Trim(), user.PasswordHash))
            {
                TempData["ErrorToast"] = "Security Vault Password must be different from your Account Login Password.";
                return RedirectToAction(nameof(Index));
            }

            user.VaultPasswordHash = PasswordHasher.HashPassword(newVaultPassword.Trim());
            user.VaultPasswordSetAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("[SECURITY AUDIT] User {UserId} changed Vault Password from Vault.", user.Id);
            TempData["SuccessToast"] = "Security Vault Password updated successfully!";
            return RedirectToAction(nameof(Index));
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

        // =========================================================================
        // PART G — QR RECOVERY (PLACEHOLDER — TO BE FINALIZED)
        // A QR-code-based vault password recovery flow was discussed but is NOT yet fully specified.
        // DO NOT IMPLEMENT THIS YET.
        // TODO: Implement QR Code scanning and private key verification flow once spec is finalized.
        // =========================================================================
    }
}
