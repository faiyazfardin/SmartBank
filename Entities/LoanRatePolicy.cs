using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartBank.Entities
{
    [Table("LoanRatePolicies")]
    public class LoanRatePolicy
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Category { get; set; } = string.Empty; // Excellent / Good / ReviewRequired

        [Required]
        public int MinScore { get; set; }

        [Required]
        public int MaxScore { get; set; }

        [Required]
        [Column(TypeName = "decimal(5,2)")]
        public decimal BaseAnnualRate { get; set; }

        [Required]
        public int MinTenureMonths { get; set; }

        [Required]
        public int MaxTenureMonths { get; set; }

        [Required]
        public bool IsActive { get; set; } = true;

        [Required]
        public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow;
    }
}
