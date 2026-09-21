using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartBank.Entities
{
    [Table("LoanAuditLogs")]
    public class LoanAuditLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public int LoanApplicationId { get; set; }

        [ForeignKey("LoanApplicationId")]
        public virtual LoanApplication? LoanApplication { get; set; }

        [Required]
        [MaxLength(100)]
        public string Action { get; set; } = string.Empty; // Approved, Rejected, PaymentRecorded, RateOverridden, etc.

        [MaxLength(100)]
        public string? FieldChanged { get; set; }

        [MaxLength(500)]
        public string? OldValue { get; set; }

        [MaxLength(500)]
        public string? NewValue { get; set; }

        [Required]
        [MaxLength(100)]
        public string PerformedBy { get; set; } = string.Empty;

        [Required]
        public DateTime PerformedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(1000)]
        public string? Note { get; set; }
    }
}
