using System;
using System.Collections.Generic;
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
        private readonly IAuthService _authService;
        private readonly IConfiguration _configuration;
        private readonly IEmailService _emailService;
        private readonly IOtpService _otpService;
        private readonly SmartBankDbContext _context;
        private readonly IWelcomeEmailService _welcomeEmailService;

        public AccountController(
            IAuthService authService,
            IConfiguration configuration,
            IEmailService emailService,
            IOtpService otpService,
            SmartBankDbContext context,
            IWelcomeEmailService welcomeEmailService)
        {
            _authService = authService;
            _configuration = configuration;
            _emailService = emailService;
            _otpService = otpService;
            _context = context;
            _welcomeEmailService = welcomeEmailService;
        }

        [HttpGet]
        public async Task<IActionResult> Login(string? returnUrl = null)
        {
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
                if (int.TryParse(userIdClaim, out var userId) && userId > 0)
                {
                    var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
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

                if (dbUser.MustChangePasswordOnNextLogin)
                {
                    return RedirectToAction("ChangePassword", "Account", new { forced = "true" });
                }

                TempData["SuccessToast"] = $"Welcome back Administrator, {dbUser.FullName}!";

                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return Redirect(returnUrl);
                }

                return RedirectToAction("Users", "Admin");
            }

            // EXCEPTION RULE: If user is logging in using the default temporary password provided via mail upon admin approval
            if (dbUser.MustChangePasswordOnNextLogin)
            {
                // OTP is NOT needed. Sign in directly and force password change.
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
                return RedirectToAction("ChangePassword", "Account", new { forced = "true" });
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

                // EXCEPTION RULE: If user account requires default temporary password change upon admin approval
                if (user.MustChangePasswordOnNextLogin)
                {
                    // OTP is NOT needed. Sign in directly and force password change.
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

                    return RedirectToAction("ChangePassword", "Account", new { forced = "true" });
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

            TempData["SuccessToast"] = $"Welcome back, {user.FullName}!";

            if (user.MustChangePasswordOnNextLogin)
            {
                return RedirectToAction("ChangePassword", "Account", new { forced = "true" });
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
    }
}
