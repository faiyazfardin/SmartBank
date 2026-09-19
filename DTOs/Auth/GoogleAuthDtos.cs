using System.ComponentModel.DataAnnotations;

namespace SmartBank.DTOs.Auth
{
    public enum GoogleAuthStatus
    {
        Authenticated,
        RequiresLinking,
        RequiresRegistration,
        Pending,
        Suspended,
        Failed
    }

    public class GoogleLoginResult
    {
        public GoogleAuthStatus Status { get; set; }
        public LoginResponse? LoginData { get; set; }
        public string? GoogleSubjectId { get; set; }
        public string? Email { get; set; }
        public string? FullName { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class CompleteGoogleRegistrationRequest
    {
        [Required]
        public string GoogleSubjectId { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required]
        [MaxLength(30)]
        public string NidNumber { get; set; } = string.Empty;

        [MinLength(6)]
        public string? Password { get; set; }
    }

    public class LinkGoogleRequest
    {
        [Required]
        public string GoogleSubjectId { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }

    public class VerifyEmailRequest
    {
        [Required]
        public string Otp { get; set; } = string.Empty;
    }
}
