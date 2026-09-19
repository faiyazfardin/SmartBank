using System;
using System.ComponentModel.DataAnnotations;

namespace SmartBank.DTOs.Transactions
{
    public class TransactionRequestDto
    {
        [Required]
        public string TransactionType { get; set; } = string.Empty; // Deposit | Withdraw | Transfer | BillPayment

        [Required]
        [Range(0.01, 1000000.00, ErrorMessage = "Amount must be greater than ৳0.")]
        public decimal Amount { get; set; }

        public string? RecipientAccount { get; set; }

        public string? BillerName { get; set; }

        public string? Reference { get; set; }
    }

    public class VerifyOtpRequestDto
    {
        [Required]
        public Guid ChallengeId { get; set; }

        [Required]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "OTP must be exactly 6 digits.")]
        public string Otp { get; set; } = string.Empty;
    }

    public class ResendOtpRequestDto
    {
        [Required]
        public Guid ChallengeId { get; set; }
    }

    public class OtpChallengeResult
    {
        public Guid ChallengeId { get; set; }
        public Guid TransactionId { get; set; }
        public string TransactionType { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string? TargetInfo { get; set; }
        public string MaskedEmail { get; set; } = string.Empty;
        public DateTime IssuedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public int CooldownRemainingSeconds { get; set; } = 60;
    }

    public class OtpVerificationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? TrackingId { get; set; }
        public decimal NewBalance { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}
