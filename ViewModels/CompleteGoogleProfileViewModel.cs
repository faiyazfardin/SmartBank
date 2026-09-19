using System.ComponentModel.DataAnnotations;

namespace SmartBank.ViewModels
{
    public class CompleteGoogleProfileViewModel
    {
        public string Email { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Username is required.")]
        [StringLength(30, MinimumLength = 3, ErrorMessage = "Username must be between 3 and 30 characters.")]
        [RegularExpression(@"^[a-zA-Z0-9_]+$", ErrorMessage = "Username must contain only letters, numbers, and underscores.")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Phone number is required.")]
        [RegularExpression(@"^\d{11}$", ErrorMessage = "Phone number must be exactly 11 digits.")]
        [DataType(DataType.PhoneNumber)]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "NID number is required.")]
        [RegularExpression(@"^\d{14,16}$", ErrorMessage = "NID must be between 14 and 16 digits.")]
        public string NidNumber { get; set; } = string.Empty;
    }
}
