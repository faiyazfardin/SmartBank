using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBank.DTOs.Auth;
using SmartBank.Services.Interfaces;

namespace SmartBank.Controllers
{
    public class AccountController : Controller
    {
        private readonly IAuthService _authService;

        public AccountController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                if (User.IsInRole("Admin"))
                {
                    return RedirectToAction("Users", "Admin");
                }
                return RedirectToAction("Index", "Dashboard");
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
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userData.UserId.ToString()),
                new Claim("sub", userData.UserId.ToString()),
                new Claim(ClaimTypes.Name, userData.Username),
                new Claim(ClaimTypes.Email, userData.Email),
                new Claim("FullName", userData.FullName),
                new Claim(ClaimTypes.Role, userData.Role)
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

            TempData["SuccessToast"] = $"Welcome back, {userData.FullName}!";

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            if (userData.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("Users", "Admin");
            }

            return RedirectToAction("Index", "Dashboard");
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
        public async Task<IActionResult> Register(string? fullName, string? firstName, string? lastName, string email, string username, string? phoneNumber, string nidNumber, string password, string confirmPassword)
        {
            if (string.IsNullOrWhiteSpace(fullName))
            {
                fullName = $"{firstName} {lastName}".Trim();
            }

            if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(nidNumber) ||
                string.IsNullOrWhiteSpace(password))
            {
                ViewBag.Error = "Please fill in all required fields, including your NID Number.";
                return View();
            }

            if (password != confirmPassword)
            {
                ViewBag.Error = "Passwords do not match.";
                return View();
            }

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var registerReq = new RegisterRequest
            {
                FullName = fullName.Trim(),
                Email = email.Trim(),
                Username = username.Trim(),
                PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim(),
                NidNumber = nidNumber.Trim(),
                Password = password,
                ConfirmPassword = confirmPassword
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

                TempData["SuccessToast"] = $"Registration submitted successfully! Your account with NID: {nidNumber.Trim()} is pending administrator verification. Once an Admin approves your account, you will be able to sign in.";
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
    }
}
