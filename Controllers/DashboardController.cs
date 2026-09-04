using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartBank.Data;
using SmartBank.Models;
using System.Threading.Tasks;

namespace SmartBank.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;

        public DashboardController(UserManager<ApplicationUser> userManager, ApplicationDbContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var currentUser = await _userManager.GetUserAsync(User);

            if (currentUser == null)
            {
                return RedirectToAction("Login", "Account", new { area = "Identity" });
            }

            var account = await _context.Accounts
                .FirstOrDefaultAsync(a => a.UserId == currentUser.Id);

            ViewBag.FullName = currentUser.FullName;
            ViewBag.Email = currentUser.Email;
            ViewBag.NID = currentUser.nID;

            return View(account);
        }
    }
}