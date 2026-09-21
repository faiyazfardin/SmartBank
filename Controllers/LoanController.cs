using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartBank.Data;
using SmartBank.DTOs.Loans;
using SmartBank.Entities;
using SmartBank.Services.Interfaces;

namespace SmartBank.Controllers
{
    [Authorize]
    public class LoanController : Controller
    {
        private readonly SmartBankDbContext _context;
        private readonly ILoanEligibilityService _eligibilityService;
        private readonly ILoanService _loanService;
        private readonly ILoanCalculatorService _calculatorService;

        public LoanController(
            SmartBankDbContext context,
            ILoanEligibilityService eligibilityService,
            ILoanService loanService,
            ILoanCalculatorService calculatorService)
        {
            _context = context;
            _eligibilityService = eligibilityService;
            _loanService = loanService;
            _calculatorService = calculatorService;
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
                RequestedAmount = Math.Min(50000m, eligibility.MaximumAmount),
                RequestedTenureMonths = 12
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
                TempData["SuccessToast"] = $"Loan Application {response.Data.ApplicationNumber} submitted successfully! Our underwriting team will review it.";
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
            var details = await _loanService.GetLoanDetailsAsync(applicationNumber, userId, isAdmin);

            if (details == null)
            {
                TempData["ErrorToast"] = "Loan application not found.";
                return RedirectToAction(isAdmin ? "Loans" : "MyApplications", isAdmin ? "Admin" : "Loan");
            }

            return View(details);
        }

        // GET: /Loan/Payments
        [HttpGet]
        public async Task<IActionResult> Payments()
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return RedirectToAction("Login", "Account");

            var apps = await _loanService.GetCustomerApplicationsAsync(userId);
            var activeLoans = apps.Where(a => a.Status == "Approved" || a.Status == "Disbursed").ToList();

            var fullDetailsList = new List<LoanDetailsDto>();
            foreach (var app in activeLoans)
            {
                var det = await _loanService.GetLoanDetailsAsync(app.ApplicationNumber, userId, false);
                if (det != null) fullDetailsList.Add(det);
            }

            return View(fullDetailsList);
        }

        // POST: /Loan/PreviewEmi (AJAX)
        [HttpPost]
        public async Task<IActionResult> PreviewEmi([FromBody] EmiPreviewRequest request)
        {
            var userId = GetCurrentUserId();
            var eligibility = await _eligibilityService.EvaluateEligibilityAsync(userId);

            decimal rate = eligibility.IndicativeRate;
            int tenure = request.TenureMonths > 0 ? request.TenureMonths : 12;
            decimal principal = request.Amount > 0 ? request.Amount : 10000m;

            var emi = _calculatorService.CalculateEmi(principal, rate, tenure);
            var totalRepayable = _calculatorService.CalculateTotalRepayable(emi, tenure);
            var schedule = _calculatorService.GenerateSchedule(principal, rate, tenure, DateTime.UtcNow.AddMonths(1));

            var response = new EmiPreviewResponse
            {
                IndicativeRate = rate,
                Emi = emi,
                TotalRepayable = totalRepayable,
                TotalInterest = Math.Max(0, totalRepayable - principal),
                SchedulePreview = schedule
            };

            return Json(response);
        }
    }
}
