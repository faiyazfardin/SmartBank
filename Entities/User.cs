using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartBank.Entities
{
    [Table("Users")]
    public class User
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [MaxLength(20)]
        public string? PhoneNumber { get; set; }

        [MaxLength(30)]
        public string? NidNumber { get; set; }

        [Required]
        [MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Role { get; set; } = "Customer";

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = "Active";

        public int FailedLoginCount { get; set; } = 0;

        public DateTime? LockedUntil { get; set; }

        public bool IsEmailVerified { get; set; } = false;

        public DateTime? EmailVerifiedAt { get; set; }

        public bool MustChangePasswordOnNextLogin { get; set; } = false;

        public DateTime? TemporaryPasswordIssuedAtUtc { get; set; }

        public string? VaultPasswordHash { get; set; }

        public DateTime? VaultPasswordSetAt { get; set; }

        [MaxLength(255)]
        public string? SecurityQuestion { get; set; }

        [MaxLength(256)]
        public string? SecurityAnswerHash { get; set; }

        public int FailedVaultAttempts { get; set; } = 0;

        public DateTime? VaultLockedUntil { get; set; }

        public bool IsFirstLogin { get; set; } = true;

        public DateTime? LastVaultUnlockAt { get; set; }

        public int RiskScore { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual ICollection<Account> Accounts { get; set; } = new List<Account>();
        public virtual ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
        public virtual ICollection<LoanApplication> LoanApplications { get; set; } = new List<LoanApplication>();
        public virtual ICollection<ExternalLogin> ExternalLogins { get; set; } = new List<ExternalLogin>();
        public virtual ICollection<OtpChallenge> OtpChallenges { get; set; } = new List<OtpChallenge>();
        public virtual ICollection<TransferRequest> TransferRequests { get; set; } = new List<TransferRequest>();
        public virtual ICollection<RiskEvent> RiskEvents { get; set; } = new List<RiskEvent>();
    }
}
