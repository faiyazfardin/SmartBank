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

            return View(account);
        }
    }
}