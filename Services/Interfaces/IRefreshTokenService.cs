using System.Threading.Tasks;
using SmartBank.Entities;

namespace SmartBank.Services.Interfaces
{
    public interface IRefreshTokenService
    {
        Task<(string RawToken, RefreshToken Entity)> GenerateRefreshTokenAsync(int userId, string? ipAddress);
        Task<(RefreshToken? Entity, string? NewRawToken)> ValidateAndRotateRefreshTokenAsync(string rawToken, int userId, string? ipAddress);
        Task<bool> RevokeRefreshTokenAsync(string rawToken, int userId);
    }
}
