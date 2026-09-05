using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartBank.Data;
using SmartBank.DTOs.Loans;
using SmartBank.Services.Interfaces;

namespace SmartBank.Controllers
{
    [Authorize]
    public class LoanController : Controller
    {
        private readonly SmartBankDbContext _context;
        private readonly ILoanEligibilityService _eligibilityService;
        private readonly ILoanService _loanService;

        public LoanController(
            SmartBankDbContext context,
            ILoanEligibilityService eligibilityService,
            ILoanService loanService)
        {
            _context = context;
            _eligibilityService = eligibilityService;
            _loanService = loanService;
        }

        private int GetCurrentUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            return int.TryParse(claim, out var id) ? id : 0;
        }

        // GET: /Loan or /Loan/Eligibility
        [HttpGet]
        public async Task<IActionResult> Eligibility()
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return RedirectToAction("Login", "Account");

            var user = await _context.Users.Include(u => u.Accounts).FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return RedirectToAction("Login", "Account");

            var eligibility = await _eligibilityService.EvaluateEligibilityAsync(userId);

            ViewBag.User = user;
            ViewBag.Account = user.Accounts.FirstOrDefault();
            ViewBag.HasPendingLoan = await _context.LoanApplications.AnyAsync(l => l.UserId == userId && l.Status == "Pending");

            return View(eligibility);
        }

        // GET: /Loan/Apply
        [HttpGet]
        public async Task<IActionResult> Apply()
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return RedirectToAction("Login", "Account");

            var user = await _context.Users.Include(u => u.Accounts).FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return RedirectToAction("Login", "Account");

            var eligibility = await _eligibilityService.EvaluateEligibilityAsync(userId);
            if (!eligibility.Eligible)
            {
                TempData["ErrorToast"] = "You are currently not eligible to apply for a loan. Please review your eligibility status.";
                return RedirectToAction("Eligibility");
            }

            var hasPending = await _context.LoanApplications.AnyAsync(l => l.UserId == userId && l.Status == "Pending");
            if (hasPending)
            {
                TempData["InfoToast"] = "You already have a loan application in 'Pending' status. Please wait for the bank review.";
                return RedirectToAction("MyApplications");
            }

            ViewBag.Eligibility = eligibility;
            ViewBag.Account = user.Accounts.FirstOrDefault();
            ViewBag.User = user;

            var model = new ApplyLoanRequest
            {
                LoanType = "Personal",
                RequestedAmount = Math.Min(50000m, eligibility.MaximumAmount)
            };

            return View(model);
        }

        // POST: /Loan/Apply
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Apply(ApplyLoanRequest request)
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return RedirectToAction("Login", "Account");

            var user = await _context.Users.Include(u => u.Accounts).FirstOrDefaultAsync(u => u.Id == userId);
            var eligibility = await _eligibilityService.EvaluateEligibilityAsync(userId);

            if (!ModelState.IsValid)
            {
                ViewBag.Eligibility = eligibility;
                ViewBag.Account = user?.Accounts.FirstOrDefault();
                ViewBag.User = user;
                return View(request);
            }

            var (status, response) = await _loanService.ApplyForLoanAsync(userId, request);

            if (status == 201 && response.Data != null)
            {
                TempData["SuccessToast"] = $"Loan Application {response.Data.ApplicationNumber} submitted successfully! Our loan team is reviewing it.";
                return RedirectToAction("Details", new { applicationNumber = response.Data.ApplicationNumber });
            }

            TempData["ErrorToast"] = response.Message ?? "Failed to submit loan application.";
            ViewBag.Eligibility = eligibility;
            ViewBag.Account = user?.Accounts.FirstOrDefault();
            ViewBag.User = user;
            return View(request);
        }

        // GET: /Loan/MyApplications
        [HttpGet]
        public async Task<IActionResult> MyApplications()
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return RedirectToAction("Login", "Account");

            var apps = await _loanService.GetCustomerApplicationsAsync(userId);
            return View(apps);
        }

        // GET: /Loan/Details/{applicationNumber}
        [HttpGet]
        public async Task<IActionResult> Details(string applicationNumber)
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return RedirectToAction("Login", "Account");

            var isAdmin = User.IsInRole("Admin");
            var app = await _loanService.GetApplicationByNumberAsync(userId, applicationNumber, isAdmin);

            if (app == null)
            {
                TempData["ErrorToast"] = "Loan application not found.";
                return RedirectToAction(isAdmin ? "Loans" : "MyApplications", isAdmin ? "Admin" : "Loan");
            }

            return View(app);
        }
    }
}
