using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SmartBank.Data;
using SmartBank.Entities;
using SmartBank.Services.Interfaces;

namespace SmartBank.Services
{
    public class RefreshTokenService : IRefreshTokenService
    {
        private readonly SmartBankDbContext _context;
        private readonly int _expiryDays;

        public RefreshTokenService(SmartBankDbContext context, IConfiguration configuration)
        {
            _context = context;
            _expiryDays = int.TryParse(configuration["Jwt:RefreshTokenExpiryDays"], out var days) ? days : 7;
        }

        private static string GenerateSecureRandomToken()
        {
            var randomBytes = new byte[64];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomBytes);
            return Convert.ToBase64String(randomBytes);
        }

        private static string HashToken(string rawToken)
        {
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawToken));
            return Convert.ToHexString(hashBytes);
        }

        public async Task<(string RawToken, RefreshToken Entity)> GenerateRefreshTokenAsync(int userId, string? ipAddress)
        {
            var rawToken = GenerateSecureRandomToken();
            var tokenHash = HashToken(rawToken);

            var refreshToken = new RefreshToken
            {
                UserId = userId,
                TokenHash = tokenHash,
                ExpiresAt = DateTime.UtcNow.AddDays(_expiryDays),
                CreatedAt = DateTime.UtcNow,
                CreatedByIp = ipAddress
            };

            _context.RefreshTokens.Add(refreshToken);
            await _context.SaveChangesAsync();

            return (rawToken, refreshToken);
        }

        public async Task<(RefreshToken? Entity, string? NewRawToken)> ValidateAndRotateRefreshTokenAsync(string rawToken, int userId, string? ipAddress)
        {
            if (string.IsNullOrWhiteSpace(rawToken))
            {
                return (null, null);
            }

            var tokenHash = HashToken(rawToken);
            var tokenEntity = await _context.RefreshTokens
                .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash && rt.UserId == userId);

            if (tokenEntity == null)
            {
                return (null, null);
            }

            // Reuse detection: If token was already revoked, someone may have compromised it!
            if (tokenEntity.IsRevoked)
            {
                // Revoke all remaining active tokens for this user for security
                var activeTokens = await _context.RefreshTokens
                    .Where(rt => rt.UserId == userId && rt.RevokedAt == null)
                    .ToListAsync();

                foreach (var activeToken in activeTokens)
                {
                    activeToken.RevokedAt = DateTime.UtcNow;
                }
                await _context.SaveChangesAsync();
                return (null, null);
            }

            if (tokenEntity.IsExpired)
            {
                return (null, null);
            }

            // Generate new rotated token
            var generatedRawToken = GenerateSecureRandomToken();
            var newTokenHash = HashToken(generatedRawToken);

            // Revoke current token
            tokenEntity.RevokedAt = DateTime.UtcNow;
            tokenEntity.ReplacedByTokenHash = newTokenHash;

            var newRefreshToken = new RefreshToken
            {
                UserId = userId,
                TokenHash = newTokenHash,
                ExpiresAt = DateTime.UtcNow.AddDays(_expiryDays),
                CreatedAt = DateTime.UtcNow,
                CreatedByIp = ipAddress
            };

            _context.RefreshTokens.Add(newRefreshToken);
            await _context.SaveChangesAsync();

            return (newRefreshToken, generatedRawToken);
        }

        public async Task<bool> RevokeRefreshTokenAsync(string rawToken, int userId)
        {
            if (string.IsNullOrWhiteSpace(rawToken))
            {
                return false;
            }

            var tokenHash = HashToken(rawToken);
            var tokenEntity = await _context.RefreshTokens
                .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash && rt.UserId == userId);

            if (tokenEntity == null || tokenEntity.IsRevoked)
            {
                return false;
            }

            tokenEntity.RevokedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }
    }
}
