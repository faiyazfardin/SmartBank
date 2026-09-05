using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartBank.Data;

namespace SmartBank.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly SmartBankDbContext _context;

        public DashboardController(SmartBankDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;

            if (!int.TryParse(userIdClaim, out var userId))
            {
                return RedirectToAction("Login", "Auth");
            }

            var user = await _context.Users
                .Include(u => u.Accounts)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            var account = user.Accounts.FirstOrDefault();

            ViewBag.FullName = user.FullName;
            ViewBag.Email = user.Email;
            ViewBag.Username = user.Username;
            ViewBag.PhoneNumber = user.PhoneNumber;

            return View(account);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateProfile(string fullName, string email, string? phoneNumber)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;

            if (!int.TryParse(userIdClaim, out var userId))
            {
                return RedirectToAction("Login", "Auth");
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            if (string.IsNullOrWhiteSpace(fullName))
            {
                TempData["Error"] = "Full Name cannot be empty.";
                return RedirectToAction("Index");
            }

            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            {
                TempData["Error"] = "Please enter a valid email address.";
                return RedirectToAction("Index");
            }

            var normEmail = email.Trim().ToLowerInvariant();
            var emailExists = await _context.Users.AnyAsync(u => u.Email.ToLower() == normEmail && u.Id != userId);
            if (emailExists)
            {
                TempData["Error"] = "Email address is already in use by another account.";
                return RedirectToAction("Index");
            }

            user.FullName = fullName.Trim();
            user.Email = normEmail;
            user.PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
            user.UpdatedAt = System.DateTime.UtcNow;

            await _context.SaveChangesAsync();
            TempData["Message"] = "Profile details updated successfully.";
            return RedirectToAction("Index");
        }
    }
}