using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartBank.Entities
{
    [Table("LoanApplications")]
    public class LoanApplication
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(30)]
        public string ApplicationNumber { get; set; } = string.Empty;

        [Required]
        public int UserId { get; set; }

        [ForeignKey("UserId")]
        public virtual User? User { get; set; }

        [Required]
        public int AccountId { get; set; }

        [ForeignKey("AccountId")]
        public virtual Account? Account { get; set; }

        [Required]
        [MaxLength(50)]
        public string LoanType { get; set; } = "Personal";

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal RequestedAmount { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal EligibleAmount { get; set; }

        public int EligibilityScore { get; set; }

        [Required]
        [MaxLength(50)]
        public string EligibilityCategory { get; set; } = "Not Eligible";

        [Required]
        [MaxLength(500)]
        public string Purpose { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        public decimal? MonthlyIncome { get; set; }

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected, Cancelled

        [MaxLength(1000)]
        public string? AdminNote { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ReviewedAt { get; set; }

        [MaxLength(100)]
        public string? ReviewedBy { get; set; }
    }
}
