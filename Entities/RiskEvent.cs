using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartBank.Entities
{
    [Table("RiskEvents")]
    public class RiskEvent
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public virtual User? User { get; set; }

        [Required]
        [MaxLength(100)]
        public string EventType { get; set; } = string.Empty;

        public int Points { get; set; }

        public int ScoreAfter { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public string? Metadata { get; set; }

        public bool IsDecay { get; set; } = false;
    }
}
