using System;
using System.Collections.Generic;

namespace SmartBank.Client.Models.Loans
{
    public class LoanEligibilityDto
    {
        public bool Eligible { get; set; }
        public int Score { get; set; }
        public string Category { get; set; } = string.Empty;
        public decimal MaximumAmount { get; set; }
        public decimal CurrentBalance { get; set; }
        public decimal AverageMonthlyBalance { get; set; }
        public int TotalTransactions { get; set; }
        public double AccountAgeMonths { get; set; }
        public List<string> Reasons { get; set; } = new();
        public Dictionary<string, int> ScoreBreakdown { get; set; } = new();
    }

    public class ApplyLoanRequest
    {
        public string LoanType { get; set; } = "Personal";
        public decimal RequestedAmount { get; set; }
        public string Purpose { get; set; } = string.Empty;
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
        public string Status { get; set; } = string.Empty;
        public string? AdminNote { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? ReviewedBy { get; set; }
    }

    public class AdminLoanReviewRequest
    {
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
