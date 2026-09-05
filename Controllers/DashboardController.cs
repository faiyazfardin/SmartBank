using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartBank.Data;
using SmartBank.DTOs.Auth;
using SmartBank.Entities;
using SmartBank.Services.Interfaces;

namespace SmartBank.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly SmartBankDbContext _context;
        private readonly IAuthService _authService;

        public DashboardController(SmartBankDbContext context, IAuthService authService)
        {
            _context = context;
            _authService = authService;
        }

        private int GetCurrentUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
            return int.TryParse(claim, out var id) ? id : 0;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            if (User.IsInRole("Admin"))
            {
                return RedirectToAction("Users", "Admin");
            }

            var userId = GetCurrentUserId();
            if (userId == 0)
            {
                return RedirectToAction("Login", "Account");
            }

            var user = await _context.Users
                .Include(u => u.Accounts)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null || user.Status == "Pending" || user.Status == "Suspended" || user.Status == "Rejected")
            {
                await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.SignOutAsync(HttpContext, Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
                TempData["InfoToast"] = "Your session has expired. Please log in again.";
                return RedirectToAction("Login", "Account");
            }

            var account = user.Accounts.FirstOrDefault();
            var transactions = account != null
                ? await _context.Transactions
                    .Where(t => t.AccountId == account.Id)
                    .OrderByDescending(t => t.Timestamp)
                    .Take(15)
                    .ToListAsync()
                : new System.Collections.Generic.List<Transaction>();

            decimal totalInflow = 0;
            decimal totalOutflow = 0;

            if (account != null)
            {
                var allTx = await _context.Transactions
                    .Where(t => t.AccountId == account.Id)
                    .ToListAsync();

                totalInflow = allTx
                    .Where(t => t.Type == TransactionType.Deposit || t.Type == TransactionType.TransferIn)
                    .Sum(t => t.Amount);

                totalOutflow = allTx
                    .Where(t => t.Type == TransactionType.Withdraw || t.Type == TransactionType.TransferOut)
                    .Sum(t => t.Amount);
            }

            ViewBag.User = user;
            ViewBag.Account = account;
            ViewBag.RecentTransactions = transactions;
            ViewBag.TotalInflow = totalInflow;
            ViewBag.TotalOutflow = totalOutflow;
            ViewBag.IsSuspended = user.Status?.Equals("Suspended", StringComparison.OrdinalIgnoreCase) == true;
            ViewBag.LockedUntil = user.LockedUntil;

            return View(account);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfile(string fullName, string email, string? phoneNumber)
        {
            var userId = GetCurrentUserId();
            if (userId == 0)
            {
                return RedirectToAction("Login", "Account");
            }

            var req = new UpdateProfileRequest
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

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmPassword)
        {
            var userId = GetCurrentUserId();
            if (userId == 0)
            {
                return RedirectToAction("Login", "Account");
            }

            if (newPassword != confirmPassword)
            {
                TempData["ErrorToast"] = "New password and confirmation do not match.";
                return RedirectToAction("Index");
            }

            var req = new ChangePasswordRequest
            {
                CurrentPassword = currentPassword,
                NewPassword = newPassword,
                ConfirmNewPassword = confirmPassword
            };

            var (status, response) = await _authService.ChangePasswordAsync(userId, req);

            if (status == 200)
            {
                TempData["SuccessToast"] = "Password changed successfully! Keep your credentials safe.";
            }
            else
            {
                TempData["ErrorToast"] = response.Message ?? "Failed to change password. Ensure your current password is correct.";
            }

            return RedirectToAction("Index");
        }
    }
}