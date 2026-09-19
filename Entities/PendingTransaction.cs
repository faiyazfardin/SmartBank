using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartBank.Entities
{
    [Table("PendingTransactions")]
    public class PendingTransaction
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public int UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public virtual User User { get; set; } = null!;

        [Required]
        public int AccountId { get; set; }

        [ForeignKey(nameof(AccountId))]
        public virtual Account Account { get; set; } = null!;

        [Required]
        [MaxLength(50)]
        public string TransactionType { get; set; } = string.Empty; // Deposit | Withdraw | Transfer | BillPayment

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [MaxLength(100)]
        public string? RecipientAccount { get; set; }

        [MaxLength(100)]
        public string? BillerName { get; set; }

        [MaxLength(200)]
        public string? Reference { get; set; }

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = "PendingOtp"; // PendingOtp | Completed | Failed | Expired

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? CompletedAt { get; set; }

        [MaxLength(50)]
        public string? TrackingId { get; set; } // TXN-xxxxxxxx
    }
}
