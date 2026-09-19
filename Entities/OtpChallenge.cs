using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartBank.Entities
{
    [Table("OtpChallenges")]
    public class OtpChallenge
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public int UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public virtual User User { get; set; } = null!;

        [Required]
        [MaxLength(256)]
        public string HashedOtp { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string TransactionType { get; set; } = string.Empty; // Deposit | Withdraw | Transfer | BillPayment

        public Guid TransactionId { get; set; } // FK -> PendingTransaction

        [ForeignKey(nameof(TransactionId))]
        public virtual PendingTransaction? PendingTransaction { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TransactionAmount { get; set; }

        [MaxLength(200)]
        public string? TargetInfo { get; set; }

        public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

        public DateTime ExpiresAt { get; set; } // IssuedAt + 120s

        public DateTime? LastSentAt { get; set; }

        public bool IsUsed { get; set; } = false;

        public DateTime? UsedAt { get; set; }

        public int AttemptCount { get; set; } = 0;

        public int MaxAttempts { get; set; } = 5;

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = "Pending"; // Pending | Verified | Expired | Locked | Used

        [NotMapped]
        public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    }
}
