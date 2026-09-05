using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SmartBank.DTOs.Loans
{
    public class LoanEligibilityResultDto
    {
        public bool Eligible { get; set; }
        public int Score { get; set; }
        public string Category { get; set; } = "Not Eligible"; // Excellent, Good, Review Required, Not Eligible
        public decimal MaximumAmount { get; set; }
        public decimal CurrentBalance { get; set; }
        public decimal AverageMonthlyBalance { get; set; }
        public int TotalTransactions { get; set; }
        public double AccountAgeMonths { get; set; }
        public List<string> Reasons { get; set; } = new List<string>();
        public Dictionary<string, int> ScoreBreakdown { get; set; } = new Dictionary<string, int>();
    }

    public class ApplyLoanRequest
    {
        [Required(ErrorMessage = "Loan type is required.")]
        public string LoanType { get; set; } = "Personal";

        [Required(ErrorMessage = "Requested amount is required.")]
        [Range(1000, 500000, ErrorMessage = "Requested amount must be between ৳1,000 and ৳500,000.")]
        public decimal RequestedAmount { get; set; }

        [Required(ErrorMessage = "Loan purpose is required.")]
        [StringLength(500, MinimumLength = 5, ErrorMessage = "Purpose must be between 5 and 500 characters.")]
        public string Purpose { get; set; } = string.Empty;

        [Range(0, 10000000, ErrorMessage = "Monthly income must be a valid non-negative number.")]
        public decimal? MonthlyIncome { get; set; }
    }

    public class LoanApplicationDto
    {
        public int Id { get; set; }
        public string ApplicationNumber { get; set; } = string.Empty;
        public int UserId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerEmail { get; set; } = string.Empty;
        public string CustomerPhone { get; set; } = string.Empty;
        public int AccountId { get; set; }
        public string AccountNumber { get; set; } = string.Empty;
        public string LoanType { get; set; } = string.Empty;
        public decimal RequestedAmount { get; set; }
        public decimal EligibleAmount { get; set; }
        public int EligibilityScore { get; set; }
        public string EligibilityCategory { get; set; } = string.Empty;
        public string Purpose { get; set; } = string.Empty;
        public decimal? MonthlyIncome { get; set; }
        public string Status { get; set; } = string.Empty; // Pending, Approved, Rejected, Cancelled
        public string? AdminNote { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? ReviewedBy { get; set; }
    }

    public class AdminLoanReviewRequest
    {
        [Required(ErrorMessage = "Review comment is required.")]
        [StringLength(1000, MinimumLength = 2, ErrorMessage = "Comment must be between 2 and 1000 characters.")]
        public string Comment { get; set; } = string.Empty;
    }

    public class AdminLoanStatsDto
    {
        public int TotalApplications { get; set; }
        public int PendingCount { get; set; }
        public int ApprovedCount { get; set; }
        public int RejectedCount { get; set; }
        public decimal TotalApprovedAmount { get; set; }
    }
}
