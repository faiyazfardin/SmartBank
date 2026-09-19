using BCrypt.Net;
using Microsoft.AspNetCore.Identity;

namespace SmartBank.Security
{
    public static class PasswordHasher
    {
        private const int WorkFactor = 10;
        private static readonly PasswordHasher<object> _identityHasher = new PasswordHasher<object>();

        public static string HashPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                throw new System.ArgumentException("Password cannot be null or empty.", nameof(password));
            }

            return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
        }

        public static bool VerifyPassword(string password, string passwordHash)
        {
            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(passwordHash))
            {
                return false;
            }

            try
            {
                // 1. Try BCrypt verification if hash starts with $2
                if (passwordHash.StartsWith("$2", System.StringComparison.Ordinal) && BCrypt.Net.BCrypt.Verify(password, passwordHash))
                {
                    return true;
                }

                // 2. Try Identity PasswordHasher verification (for Identity PBKDF2 hashes)
                var result = _identityHasher.VerifyHashedPassword(null!, passwordHash, password);
                if (result == PasswordVerificationResult.Success || result == PasswordVerificationResult.SuccessRehashNeeded)
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
