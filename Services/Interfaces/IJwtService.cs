using System.Security.Claims;
using SmartBank.Entities;

namespace SmartBank.Services.Interfaces
{
    public interface IJwtService
    {
        string GenerateToken(User user, string? accountNumber = null);
        ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
        int GetExpiryMinutes();
    }
}
