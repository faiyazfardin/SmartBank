using System.ComponentModel.DataAnnotations;

namespace SmartBank.DTOs.Auth
{
    public class UpdateProfileRequest
    {
        [Required(ErrorMessage = "Full Name is required.")]
        [MaxLength(100, ErrorMessage = "Full Name cannot exceed 100 characters.")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email Address is required.")]
        [EmailAddress(ErrorMessage = "Invalid email address format.")]
        [MaxLength(255, ErrorMessage = "Email cannot exceed 255 characters.")]
        public string Email { get; set; } = string.Empty;

        [MaxLength(20, ErrorMessage = "Phone Number cannot exceed 20 characters.")]
        public string? PhoneNumber { get; set; }
    }
}
