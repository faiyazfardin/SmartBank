using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartBank.Entities
{
    public static class TransferRequestStatus
    {
        public const string PendingOtp = "PendingOtp";
        public const string Completed = "Completed";
        public const string Expired = "Expired";
        public const string Cancelled = "Cancelled";
        public const string Failed = "Failed";
    }

    [Table("TransferRequests")]
    public class TransferRequest
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        public int SourceAccountId { get; set; }

        [Required]
        public int DestinationAccountId { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [MaxLength(200)]
        public string? Memo { get; set; }

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = TransferRequestStatus.PendingOtp;

        public int? OtpChallengeId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(5);

        public DateTime? CompletedAt { get; set; }

        // Navigation properties
        [ForeignKey(nameof(UserId))]
        public virtual User User { get; set; } = null!;

        [ForeignKey(nameof(SourceAccountId))]
        public virtual Account SourceAccount { get; set; } = null!;

        [ForeignKey(nameof(DestinationAccountId))]
        public virtual Account DestinationAccount { get; set; } = null!;

        [ForeignKey(nameof(OtpChallengeId))]
        public virtual OtpChallenge? OtpChallenge { get; set; }
    }
}
