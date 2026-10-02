using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Identity;
using SmartBank.Data;
using SmartBank.DTOs.Auth;
using SmartBank.Entities;
using SmartBank.Services;
using SmartBank.Services.Interfaces;
using SmartBank.ViewModels;

namespace SmartBank.Controllers
{
    public class AccountController : Controller
    {
        private static readonly ConcurrentDictionary<string, (int UserId, DateTime Expiration)> _resetTokens = new();

        private readonly IAuthService _authService;
        private readonly IConfiguration _configuration;
        private readonly IEmailService _emailService;
        private readonly IOtpService _otpService;
        private readonly SmartBankDbContext _context;
        private readonly IWelcomeEmailService _welcomeEmailService;
        private readonly ILogger<AccountController> _logger;

        public AccountController(
            IAuthService authService,
            IConfiguration configuration,
            IEmailService emailService,
            IOtpService otpService,
            SmartBankDbContext context,
            IWelcomeEmailService welcomeEmailService,
            ILogger<AccountController> logger)
        {
            _authService = authService;
            _configuration = configuration;
            _emailService = emailService;
            _otpService = otpService;
            _context = context;
            _welcomeEmailService = welcomeEmailService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Login(string? returnUrl = null)
        {
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
                if (int.TryParse(userIdClaim, out var userId) && userId > 0)
                {
                    var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
                    if (user != null && user.Status == "Active")
                    {
                        if (user.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
                        {
                            return RedirectToAction("Users", "Admin");
                        }
                        return RedirectToAction("Index", "Dashboard");
                    }
                }

                // If user is not found, or user status is Pending/Suspended/Rejected, sign out to prevent redirect loop
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                try { await HttpContext.SignOutAsync(GoogleDefaults.AuthenticationScheme); } catch { }
            }

            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string identifier, string password, bool rememberMe = false, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(password))
            {
                ViewBag.Error = "Please enter both your Username and Password.";
                return View();
            }

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var loginReq = new LoginRequest
            {
                Username = identifier.Trim(),
                Password = password
            };

            var (statusCode, response, retryAfter) = await _authService.LoginAsync(loginReq, ipAddress);

            if (statusCode != 200 || response.Data == null)
            {
                if (statusCode == 429 && retryAfter.HasValue)
                {
                    ViewBag.Error = $"Too many failed attempts. Please wait {retryAfter.Value} minute(s) before trying again.";
                }
                else
                {
                    ViewBag.Error = response.Message ?? "Invalid credentials. Please verify your login details.";
                }
                return View();
            }

            var userData = response.Data;
            var dbUser = await _context.Users.Include(u => u.Accounts).FirstOrDefaultAsync(u => u.Id == userData.UserId);

            if (dbUser == null)
            {
                ViewBag.Error = "User account not found.";
                return View();
            }

            // Check Account Status FIRST before sending OTP or allowing login
            if (dbUser.LockedUntil.HasValue && dbUser.LockedUntil.Value > DateTime.UtcNow)
            {
                ViewBag.Error = $"Your account has been locked/suspended by Administrator until {dbUser.LockedUntil.Value:MMM dd, yyyy HH:mm} UTC. Login access is prohibited.";
                return View();
            }

            if (!string.Equals(dbUser.Status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(dbUser.Status, "Pending", StringComparison.OrdinalIgnoreCase))
                {
                    ViewBag.Error = $"Your account with NID: {dbUser.NidNumber ?? "N/A"} is pending administrator verification. Once an Admin approves your account, you will be able to sign in.";
                }
                else
                {
                    ViewBag.Error = $"Your account status is '{dbUser.Status}'. Access is restricted. Please contact administrator support.";
                }
                return View();
            }

            // ADMIN ROLE EXEMPTION: Admin accounts bypass login OTP verification completely
            if (string.Equals(dbUser.Role, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, dbUser.Id.ToString()),
                    new Claim("sub", dbUser.Id.ToString()),
                    new Claim(ClaimTypes.Name, dbUser.Username),
                    new Claim(ClaimTypes.Email, dbUser.Email),
                    new Claim("FullName", dbUser.FullName),
                    new Claim(ClaimTypes.Role, dbUser.Role)
                };

                if (!string.IsNullOrEmpty(userData.AccountNumber))
                {
                    claims.Add(new Claim("AccountNumber", userData.AccountNumber));
                }

                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);
                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = rememberMe,
                    ExpiresUtc = rememberMe ? DateTimeOffset.UtcNow.AddDays(14) : DateTimeOffset.UtcNow.AddHours(8)
                };

                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);

                if (userData.HadFailedLoginAttempt)
                {
                    HttpContext.Session.SetString("VaultLockReason", "WrongLoginAttempt");
                    HttpContext.Session.SetString("VaultTrustRestored", "false");
                    HttpContext.Session.Remove("VaultUnlockedUserId");
                    HttpContext.Session.Remove("VaultUnlockedTimestamp");
                }
                else
                {
                    HttpContext.Session.SetString("VaultTrustRestored", "true");
                    HttpContext.Session.Remove("VaultLockReason");
                    HttpContext.Session.SetString("VaultUnlockedUserId", dbUser.Id.ToString());
                    HttpContext.Session.SetString("VaultUnlockedTimestamp", DateTime.UtcNow.ToString("O"));
                }

                TempData["SuccessToast"] = $"Welcome back Administrator, {dbUser.FullName}!";

                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return Redirect(returnUrl);
                }

                return RedirectToAction("Users", "Admin");
            }

            // EXCEPTION RULE: If user is logging in on first login or using default temporary password
            if (dbUser.IsFirstLogin || dbUser.MustChangePasswordOnNextLogin)
            {
                // OTP is NOT needed for initial forced password change. Sign in directly and force change.
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, dbUser.Id.ToString()),
                    new Claim("sub", dbUser.Id.ToString()),
                    new Claim(ClaimTypes.Name, dbUser.Username),
                    new Claim(ClaimTypes.Email, dbUser.Email),
                    new Claim("FullName", dbUser.FullName),
                    new Claim(ClaimTypes.Role, dbUser.Role)
                };

                if (!string.IsNullOrEmpty(userData.AccountNumber))
                {
                    claims.Add(new Claim("AccountNumber", userData.AccountNumber));
                }

                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = rememberMe,
                    ExpiresUtc = rememberMe ? DateTimeOffset.UtcNow.AddDays(14) : DateTimeOffset.UtcNow.AddHours(8)
                };

                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), authProperties);
                return RedirectToAction(nameof(FirstLoginPasswordChange));
            }

            // DEFAULT RULE: All Admin-verified Active accounts require 6-digit email OTP after login
            var rawOtp = _otpService.GenerateCode(6);
            var salt = Guid.NewGuid().ToString("N");
            var codeHash = _otpService.Hash(rawOtp, salt);

            var otpRecord = new OtpVerification
            {
                Id = Guid.NewGuid(),
                UserId = dbUser.Id,
                Email = dbUser.Email.Trim().ToLowerInvariant(),
                CodeHash = codeHash,
                Salt = salt,
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(2),
                AttemptCount = 0,
                IsUsed = false,
                Purpose = OtpPurpose.Login
            };

            _context.OtpVerifications.Add(otpRecord);
            await _context.SaveChangesAsync();

            await _emailService.SendOtpAsync(dbUser.Email, rawOtp);

            TempData["OtpSession_Email"] = dbUser.Email;
            TempData["OtpSession_UserId"] = dbUser.Id.ToString();
            TempData["OtpSession_FullName"] = dbUser.FullName;
            TempData["OtpSession_ReturnUrl"] = returnUrl;
            TempData["OtpSession_RememberMe"] = rememberMe ? "true" : "false";
            TempData["OtpSession_HadFailedLoginAttempt"] = userData.HadFailedLoginAttempt ? "true" : "false";

            return RedirectToAction(nameof(VerifyOtp));
        }

        [HttpGet]
        public IActionResult Register()
        {
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Index", "Dashboard");
            }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(string? fullName, string? firstName, string? lastName, string email, string username, string? phoneNumber, string nidNumber)
        {
            if (string.IsNullOrWhiteSpace(fullName))
            {
                fullName = $"{firstName} {lastName}".Trim();
            }

            if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(nidNumber))
            {
                ViewBag.Error = "Please fill in all required fields, including your NID Number.";
                return View();
            }

            // Auto-generate secure default password containing upper, lower, number & special char
            var defaultPassword = SmartBank.Helpers.PasswordGeneratorHelper.GenerateSecurePassword(10);

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var registerReq = new RegisterRequest
            {
                FullName = fullName.Trim(),
                Email = email.Trim(),
                Username = username.Trim(),
                PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim(),
                NidNumber = nidNumber.Trim(),
                Password = defaultPassword,
                ConfirmPassword = defaultPassword
            };

            try
            {
                var (statusCode, response) = await _authService.RegisterAsync(registerReq, ipAddress);

                if (statusCode != 201 && statusCode != 200)
                {
                    var errors = response.Errors != null && response.Errors.Count > 0
                        ? string.Join(", ", response.Errors)
                        : response.Message ?? "Registration failed. Please check your inputs.";
                    ViewBag.Error = errors;
                    return View();
                }

                // Dispatch welcome email with default system-generated password
                await _welcomeEmailService.SendWelcomeEmailAsync(email.Trim(), fullName.Trim(), username.Trim(), defaultPassword);

                TempData["SuccessToast"] = $"Registration submitted successfully! Your account with NID: {nidNumber.Trim()} is pending administrator verification. A system-generated default temporary password has been sent to your email address ({email.Trim()}).";
                return RedirectToAction("Login");
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.InnerException?.Message ?? ex.Message;
                return View();
            }
        }

        [AllowAnonymous]
        [HttpGet]
        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["InfoToast"] = "You have been logged out securely.";
            return RedirectToAction("Index", "Home");
        }

        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> LogoutGet()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["InfoToast"] = "You have been logged out securely.";
            return RedirectToAction("Index", "Home");
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        // --- REAL GOOGLE OAUTH 2.0 & MANDATORY OTP FLOW ---
        [HttpGet]
        public IActionResult LoginWithGoogle(string? returnUrl = null)
        {
            var redirectUrl = Url.Action(nameof(GoogleCallback), "Account", new { returnUrl });
            var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
            return Challenge(properties, GoogleDefaults.AuthenticationScheme);
        }

        [HttpGet]
        public IActionResult GoogleLogin(string? returnUrl = null) => LoginWithGoogle(returnUrl);

        [HttpGet]
        public async Task<IActionResult> GoogleCallback(string? returnUrl = null)
        {
            try
            {
                var authenticateResult = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                if (!authenticateResult.Succeeded || authenticateResult.Principal == null)
                {
                    authenticateResult = await HttpContext.AuthenticateAsync(GoogleDefaults.AuthenticationScheme);
                }

                if (!authenticateResult.Succeeded || authenticateResult.Principal == null)
                {
                    TempData["ErrorToast"] = "Google authentication was cancelled or failed.";
                    return RedirectToAction(nameof(Login));
                }

                var principal = authenticateResult.Principal;
                var email = principal.FindFirst(ClaimTypes.Email)?.Value
                    ?? principal.FindFirst("urn:google:email")?.Value
                    ?? principal.FindFirst("email")?.Value;
                var googleSubjectId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? principal.FindFirst("sub")?.Value
                    ?? principal.FindFirst("id")?.Value
                    ?? (email != null ? $"google-sub-{email.Trim().ToLowerInvariant().GetHashCode():X}" : null);
                var fullName = principal.FindFirst(ClaimTypes.Name)?.Value
                    ?? principal.FindFirst(ClaimTypes.GivenName)?.Value
                    ?? email?.Split('@')[0] ?? "Google User";

                if (string.IsNullOrWhiteSpace(email))
                {
                    TempData["ErrorToast"] = "Could not retrieve verified email from your Google account.";
                    return RedirectToAction(nameof(Login));
                }

                var cleanEmail = email.Trim().ToLowerInvariant();

                // Check if user exists in database
                var user = await _context.Users
                    .Include(u => u.Accounts)
                    .FirstOrDefaultAsync(u => u.Email.ToLower() == cleanEmail);

                if (user == null || (user.Status != null && user.Status.Equals("Rejected", StringComparison.OrdinalIgnoreCase)))
                {
                    if (user != null && user.Status.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
                    {
                        // Clean up old rejected user record so a fresh application can be created
                        var extLogins = await _context.ExternalLogins.Where(e => e.UserId == user.Id).ToListAsync();
                        _context.ExternalLogins.RemoveRange(extLogins);
                        _context.Accounts.RemoveRange(user.Accounts);
                        _context.Users.Remove(user);
                        await _context.SaveChangesAsync();
                    }

                    // New / Re-registering User -> Redirect to Complete Profile
                    TempData["GoogleReg_Email"] = cleanEmail;
                    TempData["GoogleReg_SubjectId"] = googleSubjectId;
                    TempData["GoogleReg_FullName"] = fullName;
                    return RedirectToAction(nameof(CompleteGoogleProfile));
                }

                // Rule A: Check Account Status FIRST
                if (!string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase))
                {
                    await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    try { await HttpContext.SignOutAsync(GoogleDefaults.AuthenticationScheme); } catch { }

                    if (string.Equals(user.Status, "Pending", StringComparison.OrdinalIgnoreCase))
                    {
                        TempData["ErrorToast"] = $"Your account with NID: {user.NidNumber ?? "N/A"} is pending administrator verification. Once an Admin approves your account, you will be able to sign in.";
                    }
                    else
                    {
                        TempData["ErrorToast"] = $"Your account status is '{user.Status}'. Access is restricted. Please contact support.";
                    }
                    return RedirectToAction(nameof(Login));
                }

                // ADMIN ROLE EXEMPTION: Admin accounts bypass login OTP verification completely
                if (string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase))
                {
                    var defaultAccount = user.Accounts.FirstOrDefault();
                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                        new Claim("sub", user.Id.ToString()),
                        new Claim(ClaimTypes.Name, user.Username),
                        new Claim(ClaimTypes.Email, user.Email),
                        new Claim("FullName", user.FullName),
                        new Claim(ClaimTypes.Role, user.Role)
                    };
                    if (defaultAccount != null)
                    {
                        claims.Add(new Claim("AccountNumber", defaultAccount.AccountNumber));
                    }

                    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                    var authPrincipal = new ClaimsPrincipal(identity);
                    var authProperties = new AuthenticationProperties
                    {
                        IsPersistent = true,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddDays(14)
                    };
                    await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, authPrincipal, authProperties);

                    if (user.MustChangePasswordOnNextLogin)
                    {
                        return RedirectToAction("ChangePassword", "Account", new { forced = "true" });
                    }

                    TempData["SuccessToast"] = $"Welcome back Administrator via Google, {user.FullName}!";
                    if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);

                    return RedirectToAction("Users", "Admin");
                }

                // EXCEPTION RULE: If user account requires first-time login security setup or password change
                if (user.IsFirstLogin || user.MustChangePasswordOnNextLogin)
                {
                    // OTP is NOT needed for initial forced password change. Sign in directly and force change.
                    var defaultAccount = user.Accounts.FirstOrDefault();
                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                        new Claim("sub", user.Id.ToString()),
                        new Claim(ClaimTypes.Name, user.Username),
                        new Claim(ClaimTypes.Email, user.Email),
                        new Claim("FullName", user.FullName),
                        new Claim(ClaimTypes.Role, user.Role)
                    };
                    if (defaultAccount != null)
                    {
                        claims.Add(new Claim("AccountNumber", defaultAccount.AccountNumber));
                    }

                    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                    var authPrincipal = new ClaimsPrincipal(identity);
                    var authProperties = new AuthenticationProperties
                    {
                        IsPersistent = true,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddDays(14)
                    };
                    await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, authPrincipal, authProperties);

                    return RedirectToAction(nameof(FirstLoginPasswordChange));
                }

                // DEFAULT RULE: Active Admin-verified account requires 6-digit email OTP after Google login
                var rawOtp = _otpService.GenerateCode(6);
                var salt = Guid.NewGuid().ToString("N");
                var codeHash = _otpService.Hash(rawOtp, salt);

                var otpRecord = new OtpVerification
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    Email = cleanEmail,
                    CodeHash = codeHash,
                    Salt = salt,
                    CreatedAtUtc = DateTime.UtcNow,
                    ExpiresAtUtc = DateTime.UtcNow.AddMinutes(2),
                    AttemptCount = 0,
                    IsUsed = false,
                    Purpose = OtpPurpose.GoogleLogin
                };

                _context.OtpVerifications.Add(otpRecord);
                await _context.SaveChangesAsync();

                await _emailService.SendOtpAsync(cleanEmail, rawOtp);

                TempData["OtpSession_Email"] = cleanEmail;
                TempData["OtpSession_UserId"] = user.Id.ToString();
                TempData["OtpSession_GoogleSubjectId"] = googleSubjectId;
                TempData["OtpSession_FullName"] = user.FullName;
                TempData["OtpSession_ReturnUrl"] = returnUrl;

                return RedirectToAction(nameof(VerifyOtp));
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[GOOGLE CALLBACK EXCEPTION] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                Console.ResetColor();

                TempData["ErrorToast"] = $"Google sign-in error: {ex.Message}";
                return RedirectToAction(nameof(Login));
            }
        }

        [HttpGet]
        public IActionResult VerifyOtp()
        {
            var email = TempData["OtpSession_Email"] as string;
            if (string.IsNullOrEmpty(email))
            {
                TempData["ErrorToast"] = "Your verification session expired. Please sign in again.";
                return RedirectToAction(nameof(Login));
            }

            TempData.Keep("OtpSession_Email");
            TempData.Keep("OtpSession_UserId");
            TempData.Keep("OtpSession_GoogleSubjectId");
            TempData.Keep("OtpSession_FullName");
            TempData.Keep("OtpSession_ReturnUrl");
            TempData.Keep("OtpSession_RememberMe");

            ViewBag.Email = email;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyOtp(string code)
        {
            var email = TempData["OtpSession_Email"] as string;
            var userIdStr = TempData["OtpSession_UserId"] as string;
            var googleSubjectId = TempData["OtpSession_GoogleSubjectId"] as string;
            var fullName = TempData["OtpSession_FullName"] as string ?? "Customer";
            var returnUrl = TempData["OtpSession_ReturnUrl"] as string;
            var rememberMeStr = TempData["OtpSession_RememberMe"] as string;
            bool rememberMe = rememberMeStr == "true";

            if (string.IsNullOrEmpty(email))
            {
                TempData["ErrorToast"] = "Session expired. Please restart login.";
                return RedirectToAction(nameof(Login));
            }

            TempData.Keep("OtpSession_Email");
            TempData.Keep("OtpSession_UserId");
            TempData.Keep("OtpSession_GoogleSubjectId");
            TempData.Keep("OtpSession_FullName");
            TempData.Keep("OtpSession_ReturnUrl");
            TempData.Keep("OtpSession_RememberMe");
            TempData.Keep("OtpSession_HadFailedLoginAttempt");

            if (string.IsNullOrWhiteSpace(code) || code.Length != 6)
            {
                ViewBag.Error = "Please enter the complete 6-digit code.";
                ViewBag.Email = email;
                return View();
            }

            var cleanEmail = email.Trim().ToLowerInvariant();
            var otpRecord = await _context.OtpVerifications
                .Where(o => o.Email == cleanEmail && !o.IsUsed)
                .OrderByDescending(o => o.CreatedAtUtc)
                .FirstOrDefaultAsync();

            if (otpRecord == null || otpRecord.ExpiresAtUtc < DateTime.UtcNow)
            {
                ViewBag.Error = "Your verification code has expired. Please request a new code.";
                ViewBag.Email = email;
                return View();
            }

            if (otpRecord.AttemptCount >= 5)
            {
                otpRecord.IsUsed = true;
                await _context.SaveChangesAsync();
                ViewBag.Error = "Too many failed attempts. This code is now invalidated. Please request a new one.";
                ViewBag.Email = email;
                return View();
            }

            bool isValid = _otpService.Verify(code.Trim(), otpRecord.CodeHash, otpRecord.Salt);
            if (!isValid)
            {
                otpRecord.AttemptCount++;
                await _context.SaveChangesAsync();
                ViewBag.Error = $"Invalid code. You have {5 - otpRecord.AttemptCount} attempts remaining.";
                ViewBag.Email = email;
                return View();
            }

            // Mark OTP as used
            otpRecord.IsUsed = true;
            await _context.SaveChangesAsync();

            // Retrieve user
            User? user = null;
            if (int.TryParse(userIdStr, out var uId))
            {
                user = await _context.Users.Include(u => u.Accounts).FirstOrDefaultAsync(u => u.Id == uId);
            }
            if (user == null)
            {
                user = await _context.Users.Include(u => u.Accounts).FirstOrDefaultAsync(u => u.Email.ToLower() == cleanEmail);
            }

            if (user == null)
            {
                // New user via Google -> redirect to Profile Completion
                TempData["GoogleReg_Email"] = cleanEmail;
                TempData["GoogleReg_SubjectId"] = googleSubjectId;
                TempData["GoogleReg_FullName"] = fullName;
                return RedirectToAction(nameof(CompleteGoogleProfile));
            }

            if (!string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(user.Status, "Pending", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["ErrorToast"] = $"Your account with NID: {user.NidNumber ?? "N/A"} is pending administrator verification. Once an Admin approves your account, you will be able to sign in.";
                }
                else
                {
                    TempData["ErrorToast"] = $"Your account status is '{user.Status}'. Access is restricted. Please contact support.";
                }
                return RedirectToAction(nameof(Login));
            }

            // User is Active -> Sign in with Cookie auth
            var defaultAccount = user.Accounts.FirstOrDefault();
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim("sub", user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim("FullName", user.FullName),
                new Claim(ClaimTypes.Role, user.Role)
            };
            if (defaultAccount != null)
            {
                claims.Add(new Claim("AccountNumber", defaultAccount.AccountNumber));
            }

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authPrincipal = new ClaimsPrincipal(identity);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = rememberMe,
                ExpiresUtc = rememberMe ? DateTimeOffset.UtcNow.AddDays(14) : DateTimeOffset.UtcNow.AddHours(8)
            };
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, authPrincipal, authProperties);

            // PART A — MULTIVERSAL VAULT STATE (Login-Triggered)
            var hadFailedStr = TempData["OtpSession_HadFailedLoginAttempt"] as string;
            bool hadFailedLogin = hadFailedStr == "true";
            if (hadFailedLogin)
            {
                HttpContext.Session.SetString("VaultLockReason", "WrongLoginAttempt");
                HttpContext.Session.SetString("VaultTrustRestored", "false");
                HttpContext.Session.Remove("VaultUnlockedUserId");
                HttpContext.Session.Remove("VaultUnlockedTimestamp");
                HttpContext.Session.Remove("VaultSessionKey");
                HttpContext.Session.Remove("VaultUserKey");
            }
            else
            {
                HttpContext.Session.SetString("VaultTrustRestored", "true");
                HttpContext.Session.Remove("VaultLockReason");
                HttpContext.Session.SetString("VaultUnlockedUserId", user.Id.ToString());
                HttpContext.Session.SetString("VaultUnlockedTimestamp", DateTime.UtcNow.ToString("O"));
                HttpContext.Session.SetString("VaultUserKey", user.Id.ToString());
                HttpContext.Session.SetString("VaultSessionKey", DateTime.UtcNow.ToString("O"));
            }

            TempData["SuccessToast"] = $"Welcome back, {user.FullName}!";

            if (user.IsFirstLogin || user.MustChangePasswordOnNextLogin)
            {
                return RedirectToAction(nameof(FirstLoginPasswordChange));
            }

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);

            return user.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase)
                ? RedirectToAction("Users", "Admin")
                : RedirectToAction("Index", "Dashboard");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendOtp()
        {
            var email = TempData["OtpSession_Email"] as string;
            var userIdStr = TempData["OtpSession_UserId"] as string;

            if (string.IsNullOrEmpty(email))
            {
                return Json(new { success = false, message = "Verification session expired." });
            }

            TempData.Keep("OtpSession_Email");
            TempData.Keep("OtpSession_UserId");
            TempData.Keep("OtpSession_GoogleSubjectId");
            TempData.Keep("OtpSession_FullName");
            TempData.Keep("OtpSession_ReturnUrl");
            TempData.Keep("OtpSession_RememberMe");

            var cleanEmail = email.Trim().ToLowerInvariant();

            // Check Account Status before resending
            User? user = null;
            if (int.TryParse(userIdStr, out var uId))
            {
                user = await _context.Users.FirstOrDefaultAsync(u => u.Id == uId);
            }
            if (user == null)
            {
                user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == cleanEmail);
            }

            if (user != null && !string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                return Json(new { success = false, message = $"Account status is '{user.Status}'. Verification code cannot be sent." });
            }

            // Rate Limit: 60s cooldown
            var lastOtp = await _context.OtpVerifications
                .Where(o => o.Email == cleanEmail)
                .OrderByDescending(o => o.CreatedAtUtc)
                .FirstOrDefaultAsync();

            if (lastOtp != null && (DateTime.UtcNow - lastOtp.CreatedAtUtc).TotalSeconds < 60)
            {
                var remaining = 60 - (int)(DateTime.UtcNow - lastOtp.CreatedAtUtc).TotalSeconds;
                return Json(new { success = false, message = $"Please wait {remaining} seconds before requesting a new code." });
            }

            if (lastOtp != null) lastOtp.IsUsed = true;

            var rawOtp = _otpService.GenerateCode(6);
            var salt = Guid.NewGuid().ToString("N");
            var codeHash = _otpService.Hash(rawOtp, salt);

            var newRecord = new OtpVerification
            {
                Id = Guid.NewGuid(),
                UserId = user?.Id,
                Email = cleanEmail,
                CodeHash = codeHash,
                Salt = salt,
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(2),
                Purpose = OtpPurpose.Login
            };

            _context.OtpVerifications.Add(newRecord);
            await _context.SaveChangesAsync();

            await _emailService.SendOtpAsync(cleanEmail, rawOtp);

            return Json(new { success = true, message = "New 6-digit verification code has been dispatched to your email." });
        }

        [HttpGet]
        public IActionResult CompleteGoogleProfile()
        {
            var email = TempData["GoogleReg_Email"] as string ?? TempData["GoogleEmail"] as string;
            var fullName = TempData["GoogleReg_FullName"] as string ?? TempData["GoogleFullName"] as string;
            var subjectId = TempData["GoogleReg_SubjectId"] as string ?? TempData["GoogleSubjectId"] as string;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(subjectId))
            {
                TempData["ErrorToast"] = "Registration session expired. Please sign in with Google again.";
                return RedirectToAction(nameof(Login));
            }

            TempData.Keep("GoogleReg_Email");
            TempData.Keep("GoogleReg_SubjectId");
            TempData.Keep("GoogleReg_FullName");

            ViewBag.Email = email;
            ViewBag.FullName = fullName;
            var vm = new CompleteGoogleProfileViewModel { Email = email ?? string.Empty, FullName = fullName ?? string.Empty };
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompleteGoogleProfile(CompleteGoogleProfileViewModel model)
        {
            var email = TempData["GoogleReg_Email"] as string ?? TempData["GoogleEmail"] as string;
            var googleSubjectId = TempData["GoogleReg_SubjectId"] as string ?? TempData["GoogleSubjectId"] as string;
            var fullName = TempData["GoogleReg_FullName"] as string ?? TempData["GoogleFullName"] as string ?? "Customer";

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(googleSubjectId))
            {
                TempData["ErrorToast"] = "Session expired. Please sign in with Google again.";
                return RedirectToAction(nameof(Login));
            }

            model.Email = email;
            model.FullName = fullName;

            if (!ModelState.IsValid)
            {
                ViewBag.Email = email;
                ViewBag.FullName = fullName;
                TempData.Keep("GoogleReg_Email");
                TempData.Keep("GoogleReg_SubjectId");
                TempData.Keep("GoogleReg_FullName");
                return View(model);
            }

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var req = new CompleteGoogleRegistrationRequest
            {
                GoogleSubjectId = googleSubjectId,
                Email = email,
                FullName = fullName,
                Username = model.Username.Trim(),
                PhoneNumber = model.PhoneNumber.Trim(),
                NidNumber = model.NidNumber.Trim()
            };

            var (statusCode, response) = await _authService.CompleteGoogleRegistrationAsync(req, ipAddress);
            if (statusCode != 201 || response.Data == null)
            {
                ViewBag.Error = response.Message ?? "Registration could not be completed.";
                ViewBag.Email = email;
                ViewBag.FullName = fullName;
                TempData.Keep("GoogleReg_Email");
                TempData.Keep("GoogleReg_SubjectId");
                TempData.Keep("GoogleReg_FullName");
                return View(model);
            }

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            try { await HttpContext.SignOutAsync(GoogleDefaults.AuthenticationScheme); } catch { }

            return RedirectToAction(nameof(RegistrationSubmitted));
        }

        [HttpGet]
        public IActionResult RegistrationSubmitted()
        {
            return View();
        }

        [HttpGet]
        public IActionResult CompleteGoogleRegistration() => CompleteGoogleProfile();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> CompleteGoogleRegistration(CompleteGoogleProfileViewModel model)
            => CompleteGoogleProfile(model);

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> FirstLoginPasswordChange()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                return RedirectToAction(nameof(Login));
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                return RedirectToAction(nameof(Login));
            }

            if (!user.IsFirstLogin && !user.MustChangePasswordOnNextLogin)
            {
                return RedirectToAction("Index", "Dashboard");
            }

            return View(new FirstLoginPasswordChangeViewModel());
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FirstLoginPasswordChange(FirstLoginPasswordChangeViewModel model)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                return RedirectToAction(nameof(Login));
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                return RedirectToAction(nameof(Login));
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // 1. Verify Current Account Password
            if (!SmartBank.Security.PasswordHasher.VerifyPassword(model.CurrentAccountPassword, user.PasswordHash))
            {
                ModelState.AddModelError(nameof(model.CurrentAccountPassword), "Incorrect Current Account Password.");
                return View(model);
            }

            // 2. Verify Current Vault Password
            if (!string.IsNullOrEmpty(user.VaultPasswordHash))
            {
                if (!SmartBank.Security.PasswordHasher.VerifyPassword(model.CurrentVaultPassword, user.VaultPasswordHash))
                {
                    ModelState.AddModelError(nameof(model.CurrentVaultPassword), "Incorrect Current Vault Password.");
                    return View(model);
                }
            }

            // 3. Validate Account Password Strength
            var (isAccValid, accError) = SmartBank.Security.PasswordValidator.Validate(model.NewAccountPassword);
            if (!isAccValid)
            {
                ModelState.AddModelError(nameof(model.NewAccountPassword), accError);
                return View(model);
            }

            // 4. Validate Vault Password Strength
            var (isVltValid, vltError) = SmartBank.Security.PasswordValidator.Validate(model.NewVaultPassword);
            if (!isVltValid)
            {
                ModelState.AddModelError(nameof(model.NewVaultPassword), vltError);
                return View(model);
            }

            // 5. Ensure Vault Password differs from Account Password
            if (model.NewVaultPassword.Trim() == model.NewAccountPassword.Trim())
            {
                ModelState.AddModelError(nameof(model.NewVaultPassword), "Security Vault Password must be different from your Account Login Password.");
                return View(model);
            }

            // Update user passwords and clear first login flags
            user.PasswordHash = SmartBank.Security.PasswordHasher.HashPassword(model.NewAccountPassword.Trim());
            user.VaultPasswordHash = SmartBank.Security.PasswordHasher.HashPassword(model.NewVaultPassword.Trim());
            user.VaultPasswordSetAt = DateTime.UtcNow;

            // Set Security Question & Answer (PART D / PART F)
            string question = model.SecurityQuestion == "Other" && !string.IsNullOrWhiteSpace(model.CustomSecurityQuestion)
                ? model.CustomSecurityQuestion.Trim()
                : model.SecurityQuestion.Trim();
            user.SecurityQuestion = question;
            user.SecurityAnswerHash = SmartBank.Security.PasswordHasher.HashPassword(model.SecurityAnswer.Trim().ToLowerInvariant());

            user.IsFirstLogin = false;
            user.MustChangePasswordOnNextLogin = false;
            user.TemporaryPasswordIssuedAtUtc = null;
            user.FailedVaultAttempts = 0;
            user.VaultLockedUntil = null;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            TempData["SuccessToast"] = "Both Account and Vault passwords have been updated securely! Welcome to SmartBank.";

            return user.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase)
                ? RedirectToAction("Users", "Admin")
                : RedirectToAction("Index", "Dashboard");
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> ForceSecurityQuestionSetup()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                return RedirectToAction(nameof(Login));
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                return RedirectToAction(nameof(Login));
            }

            // If already set, no need for forced setup
            if (!string.IsNullOrWhiteSpace(user.SecurityQuestion) && !string.IsNullOrWhiteSpace(user.SecurityAnswerHash))
            {
                return RedirectToAction("Index", "Dashboard");
            }

            return View(new ForceSecurityQuestionSetupViewModel());
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForceSecurityQuestionSetup(ForceSecurityQuestionSetupViewModel model)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                return RedirectToAction(nameof(Login));
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                return RedirectToAction(nameof(Login));
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            string question = model.SecurityQuestion == "Other" && !string.IsNullOrWhiteSpace(model.CustomSecurityQuestion)
                ? model.CustomSecurityQuestion.Trim()
                : model.SecurityQuestion.Trim();

            if (string.IsNullOrWhiteSpace(question))
            {
                ModelState.AddModelError(nameof(model.SecurityQuestion), "Please select or type a security question.");
                return View(model);
            }

            user.SecurityQuestion = question;
            user.SecurityAnswerHash = SmartBank.Security.PasswordHasher.HashPassword(model.SecurityAnswer.Trim().ToLowerInvariant());
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            TempData["SuccessToast"] = "Security question configured successfully!";
            return RedirectToAction("Index", "Dashboard");
        }

        [Authorize]
        [HttpGet]
        public IActionResult ChangePassword(bool forced = false)
        {
            ViewBag.IsForced = forced;
            return View();
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(string newPassword, string confirmPassword, bool forced = false)
        {
            ViewBag.IsForced = forced;

            var (isValid, errorMessage) = SmartBank.Security.PasswordValidator.Validate(newPassword);
            if (!isValid)
            {
                ViewBag.Error = errorMessage;
                return View();
            }

            if (newPassword != confirmPassword)
            {
                ViewBag.Error = "New password and confirmation password do not match.";
                return View();
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                return RedirectToAction(nameof(Login));
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                return RedirectToAction(nameof(Login));
            }

            user.PasswordHash = SmartBank.Security.PasswordHasher.HashPassword(newPassword.Trim());
            user.MustChangePasswordOnNextLogin = false;
            user.TemporaryPasswordIssuedAtUtc = null;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            TempData["SuccessToast"] = "Password updated successfully.";
            return RedirectToAction("Index", "Dashboard");
        }

        // --- EMAIL VERIFICATION FLOW ---
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> VerifyEmail()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (!int.TryParse(userIdClaim, out var userId)) return RedirectToAction("Login");

            await _authService.SendEmailVerificationOtpAsync(userId);
            return View();
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyEmail(string otp)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (!int.TryParse(userIdClaim, out var userId)) return RedirectToAction("Login");

            if (string.IsNullOrWhiteSpace(otp))
            {
                ViewBag.Error = "Please enter the 6-digit verification code.";
                return View();
            }

            var (statusCode, response) = await _authService.VerifyEmailOtpAsync(userId, otp.Trim());
            if (statusCode != 200 || !response.Success)
            {
                ViewBag.Error = response.Message ?? "Invalid verification code.";
                return View();
            }

            TempData["SuccessToast"] = "Email address successfully verified! You may now perform fund transfers.";
            return RedirectToAction("Transfer", "Transaction");
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendEmailVerificationOtp()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (!int.TryParse(userIdClaim, out var userId)) return RedirectToAction("Login");

            var (statusCode, response) = await _authService.SendEmailVerificationOtpAsync(userId);
            if (statusCode == 200)
            {
                TempData["SuccessToast"] = "A new verification code has been dispatched to your email.";
            }
            else
            {
                TempData["ErrorToast"] = response.Message ?? "Could not send verification code.";
            }
            return RedirectToAction("VerifyEmail");
        }

        // ==========================================
        // --- FORGOT PASSWORD FLOW (3 STEPS) ---
        // ==========================================

        private static string MaskEmailOptionB(string email)
        {
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
                return "u***r@d***n.com";

            var parts = email.Split('@');
            if (parts.Length != 2) return "u***r@d***n.com";

            var local = parts[0];
            var domainFull = parts[1];

            // Option B masking: local part -> first & last char, middle asterisks
            string maskedLocal;
            if (local.Length <= 2)
            {
                maskedLocal = local.Length == 1 ? $"{local}*" : $"{local[0]}*{local[1]}";
            }
            else
            {
                var firstChar = local[0];
                var lastChar = local[local.Length - 1];
                var middleAsterisks = new string('*', local.Length - 2);
                maskedLocal = $"{firstChar}{middleAsterisks}{lastChar}";
            }

            // Domain part -> first & last char of domain name, keep TLD
            var lastDot = domainFull.LastIndexOf('.');
            string maskedDomain;
            if (lastDot > 0)
            {
                var domainName = domainFull.Substring(0, lastDot);
                var tld = domainFull.Substring(lastDot);

                string maskedDomainName;
                if (domainName.Length <= 2)
                {
                    maskedDomainName = domainName.Length == 1 ? $"{domainName}*" : $"{domainName[0]}*{domainName[1]}";
                }
                else
                {
                    var firstDom = domainName[0];
                    var lastDom = domainName[domainName.Length - 1];
                    var middleAsterisksDom = new string('*', domainName.Length - 2);
                    maskedDomainName = $"{firstDom}{middleAsterisksDom}{lastDom}";
                }

                maskedDomain = $"{maskedDomainName}{tld}";
            }
            else
            {
                maskedDomain = domainFull;
            }

            return $"{maskedLocal}@{maskedDomain}";
        }

        // STEP 1: Enter Username
        [HttpGet]
        public IActionResult ForgotPassword()
        {
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Index", "Dashboard");
            }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordRequestViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var usernameInput = model.Username.Trim().ToLowerInvariant();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == usernameInput);

            // SECURITY: Generic response if username does not exist (prevents user enumeration)
            if (user == null)
            {
                TempData["Fp_IsDummy"] = "true";
                TempData["Fp_Username"] = model.Username.Trim();
                TempData["Fp_MaskedEmail"] = "u***r@d***n.com";
                TempData["Fp_ResendCount"] = "0";
                TempData["Fp_AttemptCount"] = "0";

                _logger.LogInformation("[AUDIT] Forgot Password requested for non-existent username '{Username}' from IP {IP} at {Timestamp}", model.Username, HttpContext.Connection.RemoteIpAddress?.ToString(), DateTime.UtcNow);

                return RedirectToAction(nameof(ForgotPasswordOtp));
            }

            // Real user found
            var rawOtp = _otpService.GenerateCode(6);
            var salt = Guid.NewGuid().ToString("N");
            var codeHash = _otpService.Hash(rawOtp, salt);

            // Invalidate prior unused password reset OTPs for this user
            var existingOtps = await _context.OtpVerifications
                .Where(o => o.UserId == user.Id && o.Purpose == OtpPurpose.PasswordReset && !o.IsUsed)
                .ToListAsync();
            foreach (var existing in existingOtps)
            {
                existing.IsUsed = true;
            }

            var otpRecord = new OtpVerification
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Email = user.Email.Trim().ToLowerInvariant(),
                CodeHash = codeHash,
                Salt = salt,
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5), // 5-minute expiry requirement
                AttemptCount = 0,
                IsUsed = false,
                Purpose = OtpPurpose.PasswordReset
            };

            _context.OtpVerifications.Add(otpRecord);
            await _context.SaveChangesAsync();

            // Send OTP email
            await _emailService.SendOtpAsync(user.Email, rawOtp);

            var maskedEmail = MaskEmailOptionB(user.Email);

            TempData["Fp_IsDummy"] = "false";
            TempData["Fp_UserId"] = user.Id.ToString();
            TempData["Fp_Username"] = user.Username;
            TempData["Fp_MaskedEmail"] = maskedEmail;
            TempData["Fp_OtpId"] = otpRecord.Id.ToString();
            TempData["Fp_ResendCount"] = "0";

            _logger.LogInformation("[AUDIT] Forgot Password OTP issued for Username '{Username}' (UserId: {UserId}) from IP {IP} at {Timestamp}", user.Username, user.Id, HttpContext.Connection.RemoteIpAddress?.ToString(), DateTime.UtcNow);

            return RedirectToAction(nameof(ForgotPasswordOtp));
        }

        // STEP 2: Verify OTP View
        [HttpGet]
        public IActionResult ForgotPasswordOtp()
        {
            var maskedEmail = TempData["Fp_MaskedEmail"] as string;
            if (string.IsNullOrEmpty(maskedEmail))
            {
                return RedirectToAction(nameof(ForgotPassword));
            }

            TempData.Keep("Fp_IsDummy");
            TempData.Keep("Fp_UserId");
            TempData.Keep("Fp_Username");
            TempData.Keep("Fp_MaskedEmail");
            TempData.Keep("Fp_OtpId");
            TempData.Keep("Fp_ResendCount");
            TempData.Keep("Fp_AttemptCount");

            var resendCountStr = TempData["Fp_ResendCount"] as string ?? "0";
            int.TryParse(resendCountStr, out var resendCount);

            ViewBag.MaskedEmail = maskedEmail;
            ViewBag.ResendCount = resendCount;

            return View();
        }

        // STEP 3: Verify OTP Submission
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPasswordVerifyOtp(string code)
        {
            var maskedEmail = TempData["Fp_MaskedEmail"] as string;
            if (string.IsNullOrEmpty(maskedEmail))
            {
                return RedirectToAction(nameof(ForgotPassword));
            }

            var isDummy = TempData["Fp_IsDummy"] as string == "true";
            var username = TempData["Fp_Username"] as string ?? "User";

            TempData.Keep("Fp_IsDummy");
            TempData.Keep("Fp_UserId");
            TempData.Keep("Fp_Username");
            TempData.Keep("Fp_MaskedEmail");
            TempData.Keep("Fp_OtpId");
            TempData.Keep("Fp_ResendCount");

            var resendCountStr = TempData["Fp_ResendCount"] as string ?? "0";
            int.TryParse(resendCountStr, out var resendCount);
            ViewBag.MaskedEmail = maskedEmail;
            ViewBag.ResendCount = resendCount;

            if (string.IsNullOrWhiteSpace(code) || code.Trim().Length != 6)
            {
                ViewBag.Error = "Please enter a valid 6-digit OTP code.";
                return View("ForgotPasswordOtp");
            }

            // Handle Dummy user (non-existent username)
            if (isDummy)
            {
                var dummyAttemptStr = TempData["Fp_AttemptCount"] as string ?? "0";
                int.TryParse(dummyAttemptStr, out var dummyAttempts);
                dummyAttempts++;
                TempData["Fp_AttemptCount"] = dummyAttempts.ToString();

                if (dummyAttempts >= 3)
                {
                    _logger.LogWarning("[AUDIT] Failed Forgot Password OTP attempt limit (3/3) for dummy user '{Username}' from IP {IP}", username, HttpContext.Connection.RemoteIpAddress?.ToString());
                    TempData.Remove("Fp_IsDummy");
                    TempData.Remove("Fp_MaskedEmail");
                    TempData.Remove("Fp_Username");
                    TempData["ErrorToast"] = "Too many incorrect attempts (3/3). For security reasons, your verification session has been terminated. Please start over.";
                    return RedirectToAction(nameof(ForgotPassword));
                }

                ViewBag.Error = $"Invalid verification code. Attempt {dummyAttempts} of 3.";
                return View("ForgotPasswordOtp");
            }

            // Real user OTP verification
            var userIdStr = TempData["Fp_UserId"] as string;
            if (!int.TryParse(userIdStr, out var userId))
            {
                TempData["ErrorToast"] = "Session expired. Please restart the password reset process.";
                return RedirectToAction(nameof(ForgotPassword));
            }

            var otpRecord = await _context.OtpVerifications
                .Where(o => o.UserId == userId && o.Purpose == OtpPurpose.PasswordReset && !o.IsUsed)
                .OrderByDescending(o => o.CreatedAtUtc)
                .FirstOrDefaultAsync();

            if (otpRecord == null || otpRecord.ExpiresAtUtc < DateTime.UtcNow)
            {
                // Idempotency check: If a duplicate POST just created an active reset token for this user, reuse it!
                var existingTokenKv = _resetTokens.FirstOrDefault(kv => kv.Value.UserId == userId && kv.Value.Expiration > DateTime.UtcNow);
                if (!string.IsNullOrEmpty(existingTokenKv.Key))
                {
                    _logger.LogInformation("[AUDIT] Duplicate OTP verification POST detected for user ID {UserId}. Redirecting to active reset token.", userId);
                    return RedirectToAction(nameof(ForgotPasswordReset), new { token = existingTokenKv.Key });
                }

                _logger.LogWarning("[AUDIT] Expired or invalid OTP code presented for user ID {UserId} from IP {IP}", userId, HttpContext.Connection.RemoteIpAddress?.ToString());
                ViewBag.Error = "This verification code has expired (5-minute limit) or is invalid. Please request a new code.";
                return View("ForgotPasswordOtp");
            }

            // Check if already locked out
            if (otpRecord.AttemptCount >= 3)
            {
                otpRecord.IsUsed = true;
                await _context.SaveChangesAsync();

                _logger.LogWarning("[AUDIT] Lockout on Forgot Password OTP for user ID {UserId} from IP {IP}", userId, HttpContext.Connection.RemoteIpAddress?.ToString());
                TempData.Remove("Fp_IsDummy");
                TempData.Remove("Fp_UserId");
                TempData.Remove("Fp_OtpId");
                TempData.Remove("Fp_MaskedEmail");
                TempData["ErrorToast"] = "Too many incorrect attempts (3/3). For security reasons, your verification session has been terminated. Please start over.";
                return RedirectToAction(nameof(ForgotPassword));
            }

            bool isValid = _otpService.Verify(code.Trim(), otpRecord.CodeHash, otpRecord.Salt);
            if (!isValid)
            {
                otpRecord.AttemptCount++;
                await _context.SaveChangesAsync();

                _logger.LogWarning("[AUDIT] Incorrect OTP entry ({AttemptCount}/3) for user ID {UserId} from IP {IP}", otpRecord.AttemptCount, userId, HttpContext.Connection.RemoteIpAddress?.ToString());

                if (otpRecord.AttemptCount >= 3)
                {
                    otpRecord.IsUsed = true;
                    await _context.SaveChangesAsync();

                    TempData.Remove("Fp_IsDummy");
                    TempData.Remove("Fp_UserId");
                    TempData.Remove("Fp_OtpId");
                    TempData.Remove("Fp_MaskedEmail");
                    TempData["ErrorToast"] = "Too many incorrect attempts (3/3). For security reasons, your verification session has been terminated. Please start over.";
                    return RedirectToAction(nameof(ForgotPassword));
                }

                ViewBag.Error = $"Invalid verification code. Attempt {otpRecord.AttemptCount} of 3.";
                return View("ForgotPasswordOtp");
            }

            // Successful OTP verification -> single-use invalidate OTP
            otpRecord.IsUsed = true;
            await _context.SaveChangesAsync();

            // Store server-side reset token valid for 10 minutes
            var resetToken = Guid.NewGuid().ToString("N");
            _resetTokens[resetToken] = (userId, DateTime.UtcNow.AddMinutes(10));

            // Clean up temporary OTP session state
            TempData.Remove("Fp_IsDummy");
            TempData.Remove("Fp_UserId");
            TempData.Remove("Fp_Username");
            TempData.Remove("Fp_MaskedEmail");
            TempData.Remove("Fp_OtpId");

            _logger.LogInformation("[AUDIT] Forgot Password OTP verified successfully for user ID {UserId} from IP {IP} at {Timestamp}", userId, HttpContext.Connection.RemoteIpAddress?.ToString(), DateTime.UtcNow);

            return RedirectToAction(nameof(ForgotPasswordReset), new { token = resetToken });
        }

        // Resend OTP AJAX Action
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPasswordResendOtp()
        {
            var resendCountStr = TempData["Fp_ResendCount"] as string ?? "0";
            int.TryParse(resendCountStr, out var resendCount);

            if (resendCount >= 3)
            {
                TempData.Keep("Fp_ResendCount");
                return Json(new { success = false, message = "Maximum resend limit reached (3/3). Please restart the forgot password process." });
            }

            var isDummy = TempData["Fp_IsDummy"] as string == "true";
            resendCount++;
            TempData["Fp_ResendCount"] = resendCount.ToString();

            TempData.Keep("Fp_IsDummy");
            TempData.Keep("Fp_UserId");
            TempData.Keep("Fp_Username");
            TempData.Keep("Fp_MaskedEmail");
            TempData.Keep("Fp_OtpId");
            TempData.Keep("Fp_AttemptCount");

            if (isDummy)
            {
                return Json(new { success = true, message = "A new OTP code has been sent to your registered email.", resendCount = resendCount });
            }

            var userIdStr = TempData["Fp_UserId"] as string;
            if (!int.TryParse(userIdStr, out var userId))
            {
                return Json(new { success = false, message = "Session expired. Please restart." });
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                return Json(new { success = false, message = "User not found." });
            }

            // Invalidate previous OTPs
            var existingOtps = await _context.OtpVerifications
                .Where(o => o.UserId == user.Id && o.Purpose == OtpPurpose.PasswordReset && !o.IsUsed)
                .ToListAsync();
            foreach (var existing in existingOtps)
            {
                existing.IsUsed = true;
            }

            var rawOtp = _otpService.GenerateCode(6);
            var salt = Guid.NewGuid().ToString("N");
            var codeHash = _otpService.Hash(rawOtp, salt);

            var otpRecord = new OtpVerification
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Email = user.Email.Trim().ToLowerInvariant(),
                CodeHash = codeHash,
                Salt = salt,
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
                AttemptCount = 0,
                IsUsed = false,
                Purpose = OtpPurpose.PasswordReset
            };

            _context.OtpVerifications.Add(otpRecord);
            await _context.SaveChangesAsync();

            await _emailService.SendOtpAsync(user.Email, rawOtp);

            TempData["Fp_OtpId"] = otpRecord.Id.ToString();
            TempData.Keep("Fp_OtpId");

            _logger.LogInformation("[AUDIT] Forgot Password OTP resent ({ResendCount}/3) for user ID {UserId} from IP {IP}", resendCount, user.Id, HttpContext.Connection.RemoteIpAddress?.ToString());

            return Json(new { success = true, message = "A new 6-digit OTP code has been sent to your registered email.", resendCount = resendCount });
        }

        // STEP 3 & 4: Reset Password View & Action
        [HttpGet]
        public IActionResult ForgotPasswordReset(string? token)
        {
            if (string.IsNullOrEmpty(token) || !_resetTokens.TryGetValue(token, out var info) || info.Expiration < DateTime.UtcNow)
            {
                if (!string.IsNullOrEmpty(token))
                {
                    _resetTokens.TryRemove(token, out _);
                }
                TempData["ErrorToast"] = "Your password reset session has expired or is invalid. Please start over.";
                return RedirectToAction(nameof(ForgotPassword));
            }

            var model = new ForgotPasswordResetViewModel
            {
                Token = token
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPasswordReset(ForgotPasswordResetViewModel model)
        {
            if (string.IsNullOrEmpty(model.Token) || !_resetTokens.TryGetValue(model.Token, out var info) || info.Expiration < DateTime.UtcNow)
            {
                if (!string.IsNullOrEmpty(model.Token))
                {
                    _resetTokens.TryRemove(model.Token, out _);
                }
                TempData["ErrorToast"] = "Your password reset session has expired or is invalid. Please start over.";
                return RedirectToAction(nameof(ForgotPassword));
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _context.Users.Include(u => u.Accounts).FirstOrDefaultAsync(u => u.Id == info.UserId);
            if (user == null)
            {
                _resetTokens.TryRemove(model.Token, out _);
                return RedirectToAction(nameof(ForgotPassword));
            }

            // Enforce Password Policy
            var newPass = model.NewPassword.Trim();
            if (newPass.Length < 8 ||
                !newPass.Any(char.IsUpper) ||
                !newPass.Any(char.IsLower) ||
                !newPass.Any(char.IsDigit) ||
                !newPass.Any(ch => !char.IsLetterOrDigit(ch)))
            {
                ViewBag.Error = "Password must be at least 8 characters long and contain uppercase, lowercase, digit, and special character.";
                return View(model);
            }

            // Update user password using static PasswordHasher
            user.PasswordHash = SmartBank.Security.PasswordHasher.HashPassword(newPass);
            user.IsFirstLogin = false;
            user.MustChangePasswordOnNextLogin = false;
            await _context.SaveChangesAsync();

            // Invalidate token so it CANNOT be reused
            _resetTokens.TryRemove(model.Token, out _);

            // Audit log reset event with Timestamp, IP, Username
            _logger.LogInformation("[AUDIT] Password reset successfully completed for Username: '{Username}' (UserId: {UserId}) from IP: {IP} at Timestamp: {Timestamp}",
                user.Username, user.Id, HttpContext.Connection.RemoteIpAddress?.ToString(), DateTime.UtcNow);

            // AUTOMATIC SIGN-IN (Create authentication cookie / session as a normal login would)
            var defaultAccount = user.Accounts.FirstOrDefault();
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim("sub", user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim("FullName", user.FullName),
                new Claim(ClaimTypes.Role, user.Role)
            };
            if (defaultAccount != null)
            {
                claims.Add(new Claim("AccountNumber", defaultAccount.AccountNumber));
            }

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authPrincipal = new ClaimsPrincipal(identity);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            };

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, authPrincipal, authProperties);

            TempData["SuccessToast"] = $"Your password has been reset successfully! Welcome to your command center, {user.FullName}.";

            // REDIRECT AUTOMATICALLY TO /Dashboard (normal post-login landing page)
            return RedirectToAction("Index", "Dashboard");
        }
    }
}
