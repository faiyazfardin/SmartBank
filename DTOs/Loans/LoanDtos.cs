using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using SmartBank.Entities;

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
        public decimal IndicativeRate { get; set; }
        public decimal SampleEmi { get; set; }
        public decimal SampleTotalRepayable { get; set; }
        public int SampleTenureMonths { get; set; } = 12;
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

        [Required(ErrorMessage = "Tenure is required.")]
        [Range(3, 60, ErrorMessage = "Tenure must be between 3 and 60 months.")]
        public int RequestedTenureMonths { get; set; } = 12;

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
        public string Status { get; set; } = string.Empty; // Pending, Approved, Disbursed, Rejected, Closed, Cancelled
        public string? AdminNote { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? ReviewedBy { get; set; }

        // Repayment details
        public decimal? ApprovedAmount { get; set; }
        public decimal? FinalInterestRate { get; set; }
        public int? FinalTenureMonths { get; set; }
        public decimal? FinalEmi { get; set; }
        public decimal? TotalRepayable { get; set; }
        public decimal? IndicativeRate { get; set; }
        public int? RequestedTenureMonths { get; set; }
        public DateTime? FirstInstallmentDate { get; set; }
        public DateTime? DisbursedAt { get; set; }
    }

    public class ApproveLoanDto
    {
        [Required(ErrorMessage = "Approved amount is required.")]
        [Range(5000, 500000, ErrorMessage = "Approved amount must be between ৳5,000 and ৳500,000.")]
        public decimal ApprovedAmount { get; set; }

        [Required(ErrorMessage = "Interest rate is required.")]
        [Range(1, 40, ErrorMessage = "Interest rate must be between 1% and 40%.")]
        public decimal InterestRate { get; set; }

        [Required(ErrorMessage = "Tenure is required.")]
        [Range(3, 60, ErrorMessage = "Tenure must be between 3 and 60 months.")]
        public int TenureMonths { get; set; }

        [MaxLength(1000)]
        public string? AdminNote { get; set; }
    }

    public class RecordPaymentDto
    {
        [Required(ErrorMessage = "Loan Application ID is required.")]
        public int LoanApplicationId { get; set; }

        public int? InstallmentId { get; set; }

        [Required(ErrorMessage = "Payment amount is required.")]
        [Range(0.01, 10000000, ErrorMessage = "Amount must be greater than zero.")]
        public decimal Amount { get; set; }

        public LoanPaymentMethod Method { get; set; } = LoanPaymentMethod.AccountDebit;

        [MaxLength(100)]
        public string? ReferenceNumber { get; set; }

        [MaxLength(1000)]
        public string? Notes { get; set; }
    }

    public class InstallmentScheduleItem
    {
        public int InstallmentNumber { get; set; }
        public DateTime DueDate { get; set; }
        public decimal OpeningBalance { get; set; }
        public decimal PrincipalPortion { get; set; }
        public decimal InterestPortion { get; set; }
        public decimal TotalDue { get; set; }
        public decimal ClosingBalance { get; set; }
    }

    public class LoanInstallmentDto
    {
        public int Id { get; set; }
        public int LoanApplicationId { get; set; }
        public int InstallmentNumber { get; set; }
        public DateTime DueDate { get; set; }
        public decimal OpeningBalance { get; set; }
        public decimal PrincipalPortion { get; set; }
        public decimal InterestPortion { get; set; }
        public decimal TotalDue { get; set; }
        public decimal ClosingBalance { get; set; }
        public string Status { get; set; } = "Pending";
        public decimal PaidAmount { get; set; }
        public DateTime? PaidAt { get; set; }
        public string? PaymentMethod { get; set; }
        public string? TransactionReference { get; set; }
        public decimal LateFeeApplied { get; set; }
        public decimal RemainingDue => Math.Max(0, (TotalDue + LateFeeApplied) - PaidAmount);
    }

    public class LoanPaymentDto
    {
        public int Id { get; set; }
        public int LoanApplicationId { get; set; }
        public int? InstallmentId { get; set; }
        public decimal Amount { get; set; }
        public DateTime PaymentDate { get; set; }
        public string Method { get; set; } = "AccountDebit";
        public string ReferenceNumber { get; set; } = string.Empty;
        public string RecordedBy { get; set; } = "SYSTEM";
        public string? Notes { get; set; }
    }

    public class LoanAuditLogDto
    {
        public int Id { get; set; }
        public int LoanApplicationId { get; set; }
        public string Action { get; set; } = string.Empty;
        public string? FieldChanged { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public string PerformedBy { get; set; } = string.Empty;
        public DateTime PerformedAt { get; set; }
        public string? Note { get; set; }
    }

    public class LoanDetailsDto
    {
        public LoanApplicationDto Application { get; set; } = new LoanApplicationDto();
        public List<LoanInstallmentDto> Installments { get; set; } = new List<LoanInstallmentDto>();
        public List<LoanPaymentDto> Payments { get; set; } = new List<LoanPaymentDto>();
        public List<LoanAuditLogDto> AuditLogs { get; set; } = new List<LoanAuditLogDto>();
        public decimal TotalPaid => Payments.Sum(p => p.Amount);
        public decimal TotalRemaining => Math.Max(0, (Application.TotalRepayable ?? 0) - TotalPaid);
    }

    public class EmiPreviewRequest
    {
        public decimal Amount { get; set; }
        public int TenureMonths { get; set; }
        public string? LoanType { get; set; }
    }

    public class EmiPreviewResponse
    {
        public decimal IndicativeRate { get; set; }
        public decimal Emi { get; set; }
        public decimal TotalRepayable { get; set; }
        public decimal TotalInterest { get; set; }
        public List<InstallmentScheduleItem> SchedulePreview { get; set; } = new List<InstallmentScheduleItem>();
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
        public int ActiveLoansCount { get; set; }
        public decimal TotalDisbursedAmount { get; set; }
        public decimal TotalOutstandingAmount { get; set; }
        public int OverdueInstallmentsCount { get; set; }
    }
}
