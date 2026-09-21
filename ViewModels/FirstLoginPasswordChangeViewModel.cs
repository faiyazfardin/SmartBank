using System.ComponentModel.DataAnnotations;

namespace SmartBank.ViewModels
{
    public class FirstLoginPasswordChangeViewModel
    {
        [Required(ErrorMessage = "Current Account Password is required")]
        [Display(Name = "Current Account Password")]
        [DataType(DataType.Password)]
        public string CurrentAccountPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "New Account Password is required")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters long")]
        [Display(Name = "New Account Password")]
        [DataType(DataType.Password)]
        public string NewAccountPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please confirm your new Account Password")]
        [Compare("NewAccountPassword", ErrorMessage = "The new account password and confirmation do not match")]
        [Display(Name = "Confirm New Account Password")]
        [DataType(DataType.Password)]
        public string ConfirmAccountPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Current Vault Password is required")]
        [Display(Name = "Current Vault Password")]
        [DataType(DataType.Password)]
        public string CurrentVaultPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "New Vault Password is required")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Vault password must be at least 8 characters long")]
        [Display(Name = "New Vault Password")]
        [DataType(DataType.Password)]
        public string NewVaultPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please confirm your new Vault Password")]
        [Compare("NewVaultPassword", ErrorMessage = "The new vault password and confirmation do not match")]
        [Display(Name = "Confirm New Vault Password")]
        [DataType(DataType.Password)]
        public string ConfirmVaultPassword { get; set; } = string.Empty;
    }
}
