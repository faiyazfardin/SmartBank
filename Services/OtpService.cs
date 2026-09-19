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
            int code = RandomNumberGenerator.GetInt32(100000, 1000000);
            return code.ToString("D6");
        }

        public string HashOtp(string otp)
        {
            return BCrypt.Net.BCrypt.HashPassword(otp.Trim());
        }

        public bool VerifyOtpHash(string plainOtp, string hashedOtp)
        {
            if (string.IsNullOrWhiteSpace(plainOtp) || string.IsNullOrWhiteSpace(hashedOtp))
                return false;

            try
            {
                return BCrypt.Net.BCrypt.Verify(plainOtp.Trim(), hashedOtp);
            }
            catch
            {
                return false;
            }
        }

        public async Task<(OtpChallenge Challenge, string PlainOtp)> CreateChallengeAsync(int userId, string purpose, string? referenceId, int expiryMinutes = 5)
        {
            var plainOtp = Generate6DigitOtp();
            var hash = HashOtp(plainOtp);

            var now = DateTime.UtcNow;

            var challenge = new OtpChallenge
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                HashedOtp = hash,
                TransactionType = purpose ?? "Transfer",
                TransactionId = Guid.TryParse(referenceId, out var g) ? g : Guid.NewGuid(),
                IssuedAt = now,
                ExpiresAt = now.AddSeconds(120),
                LastSentAt = now,
                IsUsed = false,
                AttemptCount = 0,
                MaxAttempts = 5,
                Status = "Pending"
            };

            _context.OtpChallenges.Add(challenge);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Generated OTP challenge for User ID {UserId}, Purpose: {Purpose}", userId, purpose);
            return (challenge, plainOtp);
        }

        public async Task<(bool IsValid, string? ErrorMessage)> ValidateChallengeAsync(int userId, string purpose, string? referenceId, string plainOtp)
        {
            var challenge = await _context.OtpChallenges
                .Where(o => o.UserId == userId && o.TransactionType == purpose)
                .OrderByDescending(o => o.IssuedAt)
                .FirstOrDefaultAsync();

            if (challenge == null)
            {
                return (false, "No active verification challenge found. Please request a new code.");
            }

            if (challenge.IsUsed || challenge.Status == "Used")
            {
                return (false, "This verification code has already been used.");
            }

            if (challenge.IsExpired || challenge.Status == "Expired")
            {
                return (false, "This verification code has expired. Please request a new code.");
            }

            if (challenge.AttemptCount >= MaxAttempts || challenge.Status == "Locked")
            {
                return (false, "Too many incorrect attempts (maximum 5). This code has been locked.");
            }

            if (!VerifyOtpHash(plainOtp, challenge.HashedOtp))
            {
                challenge.AttemptCount++;
                if (challenge.AttemptCount >= MaxAttempts)
                {
                    challenge.Status = "Locked";
                }
                await _context.SaveChangesAsync();

                var remainingAttempts = MaxAttempts - challenge.AttemptCount;
                if (remainingAttempts <= 0)
                {
                    return (false, "Invalid verification code. Maximum attempts exceeded. Code has been locked.");
                }

                return (false, $"Invalid verification code. {remainingAttempts} attempt(s) remaining.");
            }

            challenge.IsUsed = true;
            challenge.UsedAt = DateTime.UtcNow;
            challenge.Status = "Used";
            await _context.SaveChangesAsync();

            _logger.LogInformation("OTP successfully validated for User ID {UserId}, Purpose: {Purpose}", userId, purpose);
            return (true, null);
        }

        public async Task<(bool Success, string? ErrorMessage, string? NewPlainOtp)> ResendChallengeAsync(int userId, string purpose, string? referenceId, int cooldownSeconds = 60)
        {
            var challenge = await _context.OtpChallenges
                .Where(o => o.UserId == userId && o.TransactionType == purpose)
                .OrderByDescending(o => o.IssuedAt)
                .FirstOrDefaultAsync();

            if (challenge != null && challenge.LastSentAt.HasValue)
            {
                var elapsed = (DateTime.UtcNow - challenge.LastSentAt.Value).TotalSeconds;
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
                .Where(o => o.UserId == userId && o.TransactionType == purpose && !o.IsUsed)
                .ToListAsync();

            foreach (var ch in activeChallenges)
            {
                ch.Status = "Expired";
                ch.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
            }
            await _context.SaveChangesAsync();
        }
    }
}
