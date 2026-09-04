using BCrypt.Net;

namespace SmartBank.Security
{
    public static class PasswordHasher
    {
        private const int WorkFactor = 12;

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
                return BCrypt.Net.BCrypt.Verify(password, passwordHash);
            }
            catch
            {
                return false;
            }
        }
    }
}
