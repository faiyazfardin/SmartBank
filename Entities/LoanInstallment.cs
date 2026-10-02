using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartBank.Entities
{
    [Table("LoanInstallments")]
    public class LoanInstallment
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public int LoanApplicationId { get; set; }

        [ForeignKey("LoanApplicationId")]
        public virtual LoanApplication? LoanApplication { get; set; }

        [Required]
        public int InstallmentNumber { get; set; }

        [Required]
        public DateTime DueDate { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal OpeningBalance { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal PrincipalPortion { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal InterestPortion { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalDue { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal ClosingBalance { get; set; }

        [Required]
        public InstallmentStatus Status { get; set; } = InstallmentStatus.Pending;

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal PaidAmount { get; set; } = 0.00m;

        public DateTime? PaidAt { get; set; }

        [MaxLength(50)]
        public string? PaymentMethod { get; set; }

        [MaxLength(100)]
        public string? TransactionReference { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal LateFeeApplied { get; set; } = 0.00m;

        public virtual ICollection<LoanPayment> Payments { get; set; } = new List<LoanPayment>();
    }
}
