using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartBank.Data;
using SmartBank.Entities;
using SmartBank.Services.Interfaces;

namespace SmartBank.Services
{
    public class OtpService : IOtpService
    {
        private readonly SmartBankDbContext _context;
        private readonly ILogger<OtpService> _logger;
        private const int MaxAttempts = 5;

        public OtpService(SmartBankDbContext context, ILogger<OtpService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public string GenerateCode(int length = 6)
        {
            var bytes = new byte[4];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            var randInt = BitConverter.ToUInt32(bytes, 0);
            var maxNum = (int)Math.Pow(10, length);
            return (randInt % maxNum).ToString($"D{length}");
        }

        public string Hash(string code, string salt)
        {
            using var sha256 = SHA256.Create();
            var combined = Encoding.UTF8.GetBytes($"{code}:{salt}");
            var hashBytes = sha256.ComputeHash(combined);
            return Convert.ToHexString(hashBytes);
        }

        public bool Verify(string code, string hash, string salt)
        {
            var computedHash = Hash(code, salt);
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computedHash),
                Encoding.UTF8.GetBytes(hash)
            );
        }

        public string Generate6DigitOtp()
        {
            // Cryptographically secure 6-digit number between 100000 and 999999
            int code = RandomNumberGenerator.GetInt32(100000, 1000000);
            return code.ToString("D6");
        }

        public string HashOtp(string otp)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes("SmartBank_OTP_Salt_" + otp.Trim());
            var hashBytes = sha256.ComputeHash(bytes);
            return Convert.ToHexString(hashBytes);
        }

        public bool VerifyOtpHash(string plainOtp, string hashedOtp)
        {
            if (string.IsNullOrWhiteSpace(plainOtp) || string.IsNullOrWhiteSpace(hashedOtp))
                return false;

            var computedHash = HashOtp(plainOtp);
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computedHash),
                Encoding.UTF8.GetBytes(hashedOtp)
            );
        }

        public async Task<(OtpChallenge Challenge, string PlainOtp)> CreateChallengeAsync(int userId, string purpose, string? referenceId, int expiryMinutes = 5)
        {
            var plainOtp = Generate6DigitOtp();
            var hash = HashOtp(plainOtp);

            // Invalidate any existing active challenges for this user/purpose/reference
            var existingChallenges = await _context.OtpChallenges
                .Where(o => o.UserId == userId && o.Purpose == purpose && o.ReferenceId == referenceId && o.UsedAt == null && o.ExpiresAt > DateTime.UtcNow)
                .ToListAsync();

            foreach (var old in existingChallenges)
            {
                old.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
            }

            var challenge = new OtpChallenge
            {
                UserId = userId,
                Purpose = purpose,
                ReferenceId = referenceId,
                CodeHash = hash,
                ExpiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes),
                UsedAt = null,
                AttemptCount = 0,
                LastResentAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            _context.OtpChallenges.Add(challenge);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Generated OTP challenge for User ID {UserId}, Purpose: {Purpose}, Reference: {ReferenceId}", userId, purpose, referenceId);
            return (challenge, plainOtp);
        }

        public async Task<(bool IsValid, string? ErrorMessage)> ValidateChallengeAsync(int userId, string purpose, string? referenceId, string plainOtp)
        {
            var challenge = await _context.OtpChallenges
                .Where(o => o.UserId == userId && o.Purpose == purpose && o.ReferenceId == referenceId)
                .OrderByDescending(o => o.CreatedAt)
                .FirstOrDefaultAsync();

            if (challenge == null)
            {
                return (false, "No active verification challenge found. Please request a new code.");
            }

            if (challenge.IsUsed)
            {
                return (false, "This verification code has already been used.");
            }

            if (challenge.IsExpired)
            {
                return (false, "This verification code has expired (valid for 5 minutes). Please request a new code.");
            }

            if (challenge.IsLockedOut)
            {
                return (false, "Too many incorrect attempts (maximum 5). This code has been invalidated for security. Please request a new code.");
            }

            if (!VerifyOtpHash(plainOtp, challenge.CodeHash))
            {
                challenge.AttemptCount++;
                await _context.SaveChangesAsync();

                var remainingAttempts = MaxAttempts - challenge.AttemptCount;
                if (remainingAttempts <= 0)
                {
                    challenge.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
                    await _context.SaveChangesAsync();
                    return (false, "Invalid verification code. Maximum attempts exceeded. Code has been invalidated.");
                }

                return (false, $"Invalid verification code. {remainingAttempts} attempt(s) remaining.");
            }

            // Valid! Mark used
            challenge.UsedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("OTP successfully validated for User ID {UserId}, Purpose: {Purpose}, Reference: {ReferenceId}", userId, purpose, referenceId);
            return (true, null);
        }

        public async Task<(bool Success, string? ErrorMessage, string? NewPlainOtp)> ResendChallengeAsync(int userId, string purpose, string? referenceId, int cooldownSeconds = 60)
        {
            var challenge = await _context.OtpChallenges
                .Where(o => o.UserId == userId && o.Purpose == purpose && o.ReferenceId == referenceId)
                .OrderByDescending(o => o.CreatedAt)
                .FirstOrDefaultAsync();

            if (challenge != null && challenge.LastResentAt.HasValue)
            {
                var elapsed = (DateTime.UtcNow - challenge.LastResentAt.Value).TotalSeconds;
                if (elapsed < cooldownSeconds)
                {
                    var waitSec = (int)Math.Ceiling(cooldownSeconds - elapsed);
                    return (false, $"Please wait {waitSec} second(s) before requesting another code.", null);
                }
            }

            var (newChallenge, plainOtp) = await CreateChallengeAsync(userId, purpose, referenceId);
            return (true, null, plainOtp);
        }

        public async Task InvalidateChallengeAsync(int userId, string purpose, string? referenceId)
        {
            var activeChallenges = await _context.OtpChallenges
                .Where(o => o.UserId == userId && o.Purpose == purpose && o.ReferenceId == referenceId && o.UsedAt == null)
                .ToListAsync();

            foreach (var ch in activeChallenges)
            {
                ch.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
            }
            await _context.SaveChangesAsync();
        }
    }
}
