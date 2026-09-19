using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SmartBank.Helpers
{
    public static class PasswordGeneratorHelper
    {
        private const string UppercaseChars = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        private const string LowercaseChars = "abcdefghijkmnopqrstuvwxyz";
        private const string DigitChars = "23456789";
        private const string SpecialChars = "!@#$%^&*";
        private const string AllAllowedChars = UppercaseChars + LowercaseChars + DigitChars + SpecialChars;

        /// <summary>
        /// Generates a cryptographically secure random password of specified length.
        /// Guarantees at least 1 uppercase, 1 lowercase, 1 digit, and 1 special character.
        /// </summary>
        public static string GenerateSecurePassword(int length = 10)
        {
            if (length < 8) length = 10;

            Span<byte> randomBytes = stackalloc byte[length + 4];
            RandomNumberGenerator.Fill(randomBytes);

            char[] password = new char[length];
            
            // Guarantee required character sets
            password[0] = UppercaseChars[randomBytes[0] % UppercaseChars.Length];
            password[1] = LowercaseChars[randomBytes[1] % LowercaseChars.Length];
            password[2] = DigitChars[randomBytes[2] % DigitChars.Length];
            password[3] = SpecialChars[randomBytes[3] % SpecialChars.Length];

            // Fill remaining characters
            for (int i = 4; i < length; i++)
            {
                password[i] = AllAllowedChars[randomBytes[i] % AllAllowedChars.Length];
            }

            // Cryptographically shuffle the array
            for (int i = password.Length - 1; i > 0; i--)
            {
                int swapIndex = randomBytes[i + 4] % (i + 1);
                (password[i], password[swapIndex]) = (password[swapIndex], password[i]);
            }

            return new string(password);
        }
    }
}
