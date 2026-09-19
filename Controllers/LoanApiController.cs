using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBank.DTOs.Common;
using SmartBank.DTOs.Loans;
using SmartBank.Services.Interfaces;

namespace SmartBank.Controllers
{
    [ApiController]
    [Authorize]
    public class LoanApiController : ControllerBase
    {
        private readonly ILoanEligibilityService _eligibilityService;
        private readonly ILoanService _loanService;

        public LoanApiController(ILoanEligibilityService eligibilityService, ILoanService loanService)
        {
            _eligibilityService = eligibilityService;
            _loanService = loanService;
        }

        private int GetCurrentUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            return int.TryParse(claim, out var id) ? id : 0;
        }

        private string GetCurrentUsername()
        {
            return User.FindFirst(ClaimTypes.Name)?.Value ?? User.Identity?.Name ?? "Admin";
        }

        /// <summary>
        /// Evaluate loan eligibility for the currently logged-in customer.
        /// </summary>
        [HttpGet("api/loans/eligibility")]
        public async Task<IActionResult> CheckEligibility()
        {
            var userId = GetCurrentUserId();
            if (userId == 0)
            {
                return Unauthorized(ApiResponse<LoanEligibilityResultDto>.FailureResponse("Unauthorized request."));
            }

            var eligibility = await _eligibilityService.EvaluateEligibilityAsync(userId);
            return Ok(ApiResponse<LoanEligibilityResultDto>.SuccessResponse(eligibility, "Loan eligibility calculated successfully."));
        }

        /// <summary>
        /// Apply for a loan with full server-side eligibility re-validation.
        /// </summary>
        [HttpPost("api/loans/apply")]
        public async Task<IActionResult> ApplyForLoan([FromBody] ApplyLoanRequest request)
        {
            if (!ModelState.IsValid)
            {
                var errors = new List<string>();
                foreach (var state in ModelState.Values)
                {
                    foreach (var error in state.Errors)
                    {
                        errors.Add(error.ErrorMessage);
                    }
                }
                return BadRequest(ApiResponse<LoanApplicationDto>.FailureResponse("Validation failed.", errors));
            }

            var userId = GetCurrentUserId();
            if (userId == 0)
            {
                return Unauthorized(ApiResponse<LoanApplicationDto>.FailureResponse("Unauthorized request."));
            }

            var (status, response) = await _loanService.ApplyForLoanAsync(userId, request);
            return StatusCode(status, response);
        }

        /// <summary>
        /// Get all loan applications submitted by the logged-in customer.
        /// </summary>
        [HttpGet("api/loans/my-applications")]
        public async Task<IActionResult> GetMyApplications()
        {
            var userId = GetCurrentUserId();
            if (userId == 0)
            {
                return Unauthorized(ApiResponse<List<LoanApplicationDto>>.FailureResponse("Unauthorized request."));
            }

            var apps = await _loanService.GetCustomerApplicationsAsync(userId);
            return Ok(ApiResponse<List<LoanApplicationDto>>.SuccessResponse(apps, "Loan applications retrieved successfully."));
        }

        /// <summary>
        /// Get details of a specific loan application. Customers can only view their own.
        /// </summary>
        [HttpGet("api/loans/{applicationNumber}")]
        public async Task<IActionResult> GetLoanDetails(string applicationNumber)
        {
            var userId = GetCurrentUserId();
            if (userId == 0)
            {
                return Unauthorized(ApiResponse<LoanApplicationDto>.FailureResponse("Unauthorized request."));
            }

            var isAdmin = User.IsInRole("Admin");
            var app = await _loanService.GetApplicationByNumberAsync(userId, applicationNumber, isAdmin);

            if (app == null)
            {
                return NotFound(ApiResponse<LoanApplicationDto>.FailureResponse("Loan application not found or access denied."));
            }

            return Ok(ApiResponse<LoanApplicationDto>.SuccessResponse(app, "Loan details retrieved successfully."));
        }

        // ==========================================
        // ADMIN ENDPOINTS
        // ==========================================

        /// <summary>
        /// Get all loan applications for Admin review.
        /// </summary>
        [HttpGet("api/admin/loans")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAdminLoans([FromQuery] string? status)
        {
            var apps = await _loanService.GetAllApplicationsForAdminAsync(status);
            return Ok(ApiResponse<List<LoanApplicationDto>>.SuccessResponse(apps, "Loan applications retrieved successfully."));
        }

        /// <summary>
        /// Get system-wide loan summary statistics for Admin dashboard.
        /// </summary>
        [HttpGet("api/admin/loans/stats")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAdminLoanStats()
        {
            var stats = await _loanService.GetAdminLoanStatsAsync();
            return Ok(ApiResponse<AdminLoanStatsDto>.SuccessResponse(stats, "Loan statistics retrieved successfully."));
        }

        /// <summary>
        /// Approve a pending loan application.
        /// </summary>
        [HttpPost("api/admin/loans/{applicationNumber}/approve")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ApproveLoan(string applicationNumber, [FromBody] AdminLoanReviewRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ApiResponse<LoanApplicationDto>.FailureResponse("Review comment is required."));
            }

            var adminUsername = GetCurrentUsername();
            var (status, response) = await _loanService.ReviewApplicationAsync(applicationNumber, adminUsername, isApprove: true, request.Comment);
            return StatusCode(status, response);
        }

        /// <summary>
        /// Reject a pending loan application.
        /// </summary>
        [HttpPost("api/admin/loans/{applicationNumber}/reject")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> RejectLoan(string applicationNumber, [FromBody] AdminLoanReviewRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ApiResponse<LoanApplicationDto>.FailureResponse("Review comment is required."));
            }

            var adminUsername = GetCurrentUsername();
            var (status, response) = await _loanService.ReviewApplicationAsync(applicationNumber, adminUsername, isApprove: false, request.Comment);
            return StatusCode(status, response);
        }
    }
}
