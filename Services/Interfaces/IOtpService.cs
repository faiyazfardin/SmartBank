using System;
using System.Threading.Tasks;
using SmartBank.Entities;

namespace SmartBank.Services.Interfaces
{
    public interface IOtpService
    {
        string GenerateCode(int length = 6);
        string Hash(string code, string salt);
        bool Verify(string code, string hash, string salt);

        string Generate6DigitOtp();
        string HashOtp(string otp);
        bool VerifyOtpHash(string plainOtp, string hashedOtp);

        Task<(OtpChallenge Challenge, string PlainOtp)> CreateChallengeAsync(int userId, string purpose, string? referenceId, int expiryMinutes = 5);
        Task<(bool IsValid, string? ErrorMessage)> ValidateChallengeAsync(int userId, string purpose, string? referenceId, string plainOtp);
        Task<(bool Success, string? ErrorMessage, string? NewPlainOtp)> ResendChallengeAsync(int userId, string purpose, string? referenceId, int cooldownSeconds = 60);
        Task InvalidateChallengeAsync(int userId, string purpose, string? referenceId);
    }
}
