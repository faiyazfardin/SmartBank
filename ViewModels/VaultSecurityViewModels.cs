using System.ComponentModel.DataAnnotations;

namespace SmartBank.ViewModels
{
    public class ForceSecurityQuestionSetupViewModel
    {
        [Required(ErrorMessage = "Please select a security question")]
        [Display(Name = "Security Question")]
        public string SecurityQuestion { get; set; } = string.Empty;

        [Display(Name = "Custom Security Question")]
        public string? CustomSecurityQuestion { get; set; }

        [Required(ErrorMessage = "Security answer is required")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Answer must be at least 2 characters long")]
        [Display(Name = "Security Answer")]
        public string SecurityAnswer { get; set; } = string.Empty;
    }

    public class VerifySecurityQuestionViewModel
    {
        public string SecurityQuestion { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter your security answer")]
        [Display(Name = "Your Security Answer")]
        public string SecurityAnswer { get; set; } = string.Empty;
    }

    public class ResetVaultPasswordViewModel
    {
        [Required]
        public string Token { get; set; } = string.Empty;

        [Required(ErrorMessage = "New Vault Password is required")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Vault password must be at least 8 characters long")]
        [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).{8,}$",
            ErrorMessage = "Vault password must contain uppercase, lowercase, digit, and special character.")]
        [DataType(DataType.Password)]
        [Display(Name = "New Vault Password")]
        public string NewVaultPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please confirm your new Vault Password")]
        [Compare("NewVaultPassword", ErrorMessage = "New vault password and confirmation do not match.")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm New Vault Password")]
        public string ConfirmVaultPassword { get; set; } = string.Empty;
    }

    public class ChangeSecurityQuestionViewModel
    {
        [Required(ErrorMessage = "Current Vault Password is required")]
        [DataType(DataType.Password)]
        [Display(Name = "Current Vault Password")]
        public string CurrentVaultPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please select a new security question")]
        [Display(Name = "New Security Question")]
        public string SecurityQuestion { get; set; } = string.Empty;

        [Display(Name = "Custom Security Question")]
        public string? CustomSecurityQuestion { get; set; }

        [Required(ErrorMessage = "New security answer is required")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Answer must be at least 2 characters long")]
        [Display(Name = "New Security Answer")]
        public string SecurityAnswer { get; set; } = string.Empty;
    }
}
