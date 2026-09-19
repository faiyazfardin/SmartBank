using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartBank.Entities
{
    public enum OtpPurpose
    {
        GoogleLogin = 1,
        Registration = 2,
        PasswordReset = 3,
        TransactionApproval = 4
    }

    [Table("OtpVerifications")]
    public class OtpVerification
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public int? UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public virtual User? User { get; set; }

        [Required]
        [MaxLength(255)]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MaxLength(256)]
        public string CodeHash { get; set; } = string.Empty;

        [Required]
        [MaxLength(64)]
        public string Salt { get; set; } = string.Empty;

        public DateTime ExpiresAtUtc { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public int AttemptCount { get; set; } = 0;

        public bool IsUsed { get; set; } = false;

        public OtpPurpose Purpose { get; set; } = OtpPurpose.GoogleLogin;
    }
}
