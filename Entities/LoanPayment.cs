using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartBank.Entities
{
    [Table("LoanPayments")]
    public class LoanPayment
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public int LoanApplicationId { get; set; }

        [ForeignKey("LoanApplicationId")]
        public virtual LoanApplication? LoanApplication { get; set; }

        public int? InstallmentId { get; set; }

        [ForeignKey("InstallmentId")]
        public virtual LoanInstallment? Installment { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [Required]
        public DateTime PaymentDate { get; set; } = DateTime.UtcNow;

        [Required]
        public LoanPaymentMethod Method { get; set; } = LoanPaymentMethod.AccountDebit;

        [Required]
        [MaxLength(100)]
        public string ReferenceNumber { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string RecordedBy { get; set; } = "SYSTEM";

        [MaxLength(1000)]
        public string? Notes { get; set; }
    }
}
