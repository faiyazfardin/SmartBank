using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SmartBank.Data;
using SmartBank.DTOs.Common;
using SmartBank.DTOs.Loans;
using SmartBank.Entities;
using SmartBank.Services.Interfaces;

namespace SmartBank.Services
{
    public class LoanService : ILoanService
    {
        private readonly SmartBankDbContext _context;
        private readonly ILoanEligibilityService _eligibilityService;

        public LoanService(SmartBankDbContext context, ILoanEligibilityService eligibilityService)
        {
            _context = context;
            _eligibilityService = eligibilityService;
        }

        public async Task<(int StatusCode, ApiResponse<LoanApplicationDto> Response)> ApplyForLoanAsync(int userId, ApplyLoanRequest request)
        {
            var user = await _context.Users
                .Include(u => u.Accounts)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                return (404, ApiResponse<LoanApplicationDto>.FailureResponse("User record not found."));
            }

            var account = user.Accounts.FirstOrDefault();
            if (account == null)
            {
                return (400, ApiResponse<LoanApplicationDto>.FailureResponse("No active banking account found for this user."));
            }

            if (!account.IsActive || user.Status.Equals("Suspended", StringComparison.OrdinalIgnoreCase))
            {
                return (403, ApiResponse<LoanApplicationDto>.FailureResponse("Your account must be active to apply for a loan."));
            }

            if (string.IsNullOrWhiteSpace(user.NidNumber))
            {
                return (400, ApiResponse<LoanApplicationDto>.FailureResponse("KYC verification is required. Please update your profile with a valid NID before applying."));
            }

            // Check for existing pending application
            var hasPending = await _context.LoanApplications
                .AnyAsync(l => l.UserId == userId && l.Status == "Pending");

            if (hasPending)
            {
                return (400, ApiResponse<LoanApplicationDto>.FailureResponse("You already have a pending loan application. Please wait for bank review."));
            }

            // SERVER-SIDE ELIGIBILITY RE-EVALUATION (Never trust client scores)
            var eligibility = await _eligibilityService.EvaluateEligibilityAsync(userId);

            if (!eligibility.Eligible)
            {
                var reasonsStr = string.Join("; ", eligibility.Reasons.Where(r => r.StartsWith("✗")));
                return (400, ApiResponse<LoanApplicationDto>.FailureResponse(
                    "You are not currently eligible for a loan.",
                    string.IsNullOrWhiteSpace(reasonsStr) ? "Eligibility score did not meet the required threshold." : reasonsStr));
            }

            if (request.RequestedAmount <= 0)
            {
                return (400, ApiResponse<LoanApplicationDto>.FailureResponse("Requested amount must be greater than zero."));
            }

            if (request.RequestedAmount > eligibility.MaximumAmount)
            {
                return (400, ApiResponse<LoanApplicationDto>.FailureResponse(
                    $"Requested amount exceeds your maximum eligible amount of ৳{eligibility.MaximumAmount:N2}."));
            }

            // Generate sequential application number
            var currentCount = await _context.LoanApplications.CountAsync();
            var appNumber = $"LN-{(currentCount + 1):D5}";

            // Ensure uniqueness
            while (await _context.LoanApplications.AnyAsync(l => l.ApplicationNumber == appNumber))
            {
                currentCount++;
                appNumber = $"LN-{(currentCount + 1):D5}";
            }

            var loanApp = new LoanApplication
            {
                ApplicationNumber = appNumber,
                UserId = user.Id,
                AccountId = account.Id,
                LoanType = request.LoanType ?? "Personal",
                RequestedAmount = request.RequestedAmount,
                EligibleAmount = eligibility.MaximumAmount,
                EligibilityScore = eligibility.Score,
                EligibilityCategory = eligibility.Category,
                Purpose = request.Purpose?.Trim() ?? string.Empty,
                MonthlyIncome = request.MonthlyIncome,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };

            _context.LoanApplications.Add(loanApp);
            await _context.SaveChangesAsync();

            var dto = MapToDto(loanApp, user, account);
            return (201, ApiResponse<LoanApplicationDto>.SuccessResponse(dto, "Loan application submitted successfully."));
        }

        public async Task<List<LoanApplicationDto>> GetCustomerApplicationsAsync(int userId)
        {
            var apps = await _context.LoanApplications
                .Include(l => l.User)
                .Include(l => l.Account)
                .Where(l => l.UserId == userId)
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();

            return apps.Select(a => MapToDto(a, a.User, a.Account)).ToList();
        }

        public async Task<LoanApplicationDto?> GetApplicationByNumberAsync(int userId, string applicationNumber, bool isAdmin = false)
        {
            var query = _context.LoanApplications
                .Include(l => l.User)
                .Include(l => l.Account)
                .Where(l => l.ApplicationNumber == applicationNumber);

            if (!isAdmin)
            {
                query = query.Where(l => l.UserId == userId);
            }

            var app = await query.FirstOrDefaultAsync();
            if (app == null) return null;

            return MapToDto(app, app.User, app.Account);
        }

        public async Task<List<LoanApplicationDto>> GetAllApplicationsForAdminAsync(string? statusFilter = null)
        {
            var query = _context.LoanApplications
                .Include(l => l.User)
                .Include(l => l.Account)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(statusFilter))
            {
                query = query.Where(l => l.Status == statusFilter);
            }

            var apps = await query
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();

            return apps.Select(a => MapToDto(a, a.User, a.Account)).ToList();
        }

        public async Task<(int StatusCode, ApiResponse<LoanApplicationDto> Response)> ReviewApplicationAsync(
            string applicationNumber, string adminUsername, bool isApprove, string comment)
        {
            var app = await _context.LoanApplications
                .Include(l => l.User)
                .Include(l => l.Account)
                .FirstOrDefaultAsync(l => l.ApplicationNumber == applicationNumber);

            if (app == null)
            {
                return (404, ApiResponse<LoanApplicationDto>.FailureResponse("Loan application not found."));
            }

            if (app.Status != "Pending")
            {
                return (400, ApiResponse<LoanApplicationDto>.FailureResponse($"This application has already been marked as '{app.Status}'."));
            }

            app.Status = isApprove ? "Approved" : "Rejected";
            app.ReviewedAt = DateTime.UtcNow;
            app.ReviewedBy = adminUsername;
            app.AdminNote = comment?.Trim();

            await _context.SaveChangesAsync();

            var dto = MapToDto(app, app.User, app.Account);
            var actionName = isApprove ? "approved" : "rejected";
            return (200, ApiResponse<LoanApplicationDto>.SuccessResponse(dto, $"Loan application {actionName} successfully."));
        }

        public async Task<AdminLoanStatsDto> GetAdminLoanStatsAsync()
        {
            var total = await _context.LoanApplications.CountAsync();
            var pending = await _context.LoanApplications.CountAsync(l => l.Status == "Pending");
            var approved = await _context.LoanApplications.CountAsync(l => l.Status == "Approved");
            var rejected = await _context.LoanApplications.CountAsync(l => l.Status == "Rejected");
            var totalApprovedAmount = await _context.LoanApplications
                .Where(l => l.Status == "Approved")
                .SumAsync(l => (decimal?)l.RequestedAmount) ?? 0m;

            return new AdminLoanStatsDto
            {
                TotalApplications = total,
                PendingCount = pending,
                ApprovedCount = approved,
                RejectedCount = rejected,
                TotalApprovedAmount = totalApprovedAmount
            };
        }

        private static LoanApplicationDto MapToDto(LoanApplication app, User? user, Account? account)
        {
            return new LoanApplicationDto
            {
                Id = app.Id,
                ApplicationNumber = app.ApplicationNumber,
                UserId = app.UserId,
                CustomerName = user?.FullName ?? "Unknown",
                CustomerEmail = user?.Email ?? "N/A",
                CustomerPhone = user?.PhoneNumber ?? "N/A",
                AccountId = app.AccountId,
                AccountNumber = account?.AccountNumber ?? "N/A",
                LoanType = app.LoanType,
                RequestedAmount = app.RequestedAmount,
                EligibleAmount = app.EligibleAmount,
                EligibilityScore = app.EligibilityScore,
                EligibilityCategory = app.EligibilityCategory,
                Purpose = app.Purpose,
                MonthlyIncome = app.MonthlyIncome,
                Status = app.Status,
                AdminNote = app.AdminNote,
                CreatedAt = app.CreatedAt,
                ReviewedAt = app.ReviewedAt,
                ReviewedBy = app.ReviewedBy
            };
        }
    }
}
