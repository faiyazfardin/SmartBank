using System.Linq;

namespace SmartBank.Client.Security
{
    public static class PasswordValidator
    {
        public static (bool IsValid, string ErrorMessage) Validate(string? password)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                return (false, "Password cannot be empty.");
            }

            if (password.Length < 8)
            {
                return (false, "Password must be at least 8 characters long.");
            }

            if (!password.Any(char.IsUpper))
            {
                return (false, "Password must contain at least one uppercase letter (A-Z).");
            }

            if (!password.Any(char.IsLower))
            {
                return (false, "Password must contain at least one lowercase letter (a-z).");
            }

            if (!password.Any(char.IsDigit))
            {
                return (false, "Password must contain at least one number (0-9).");
            }

            if (!password.Any(ch => !char.IsLetterOrDigit(ch)))
            {
                return (false, "Password must contain at least one special character (e.g. !@#$%^&*).");
            }

            return (true, string.Empty);
        }
    }
}
