using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartBank.Entities
{
    [Table("OtpChallenges")]
    public class OtpChallenge
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        [MaxLength(50)]
        public string Purpose { get; set; } = "Transfer"; // "Transfer", "EmailVerification", etc.

        [MaxLength(100)]
        public string? ReferenceId { get; set; } // e.g. TransferRequestId

        [Required]
        [MaxLength(256)]
        public string CodeHash { get; set; } = string.Empty; // SHA256 / BCrypt hash of 6-digit OTP

        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(5);

        public DateTime? UsedAt { get; set; }

        public int AttemptCount { get; set; } = 0;

        public DateTime? LastResentAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [NotMapped]
        public bool IsExpired => DateTime.UtcNow > ExpiresAt;

        [NotMapped]
        public bool IsUsed => UsedAt.HasValue;

        [NotMapped]
        public bool IsLockedOut => AttemptCount >= 5;

        // Navigation property
        [ForeignKey(nameof(UserId))]
        public virtual User User { get; set; } = null!;
    }
}
