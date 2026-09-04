using System.Threading.Tasks;
using SmartBank.DTOs.Auth;
using SmartBank.DTOs.Common;

namespace SmartBank.Services.Interfaces
{
    public interface IAuthService
    {
        Task<(int StatusCode, ApiResponse<RegisterResponse> Response)> RegisterAsync(RegisterRequest request, string? ipAddress);
        Task<(int StatusCode, ApiResponse<LoginResponse> Response, int? RetryAfterMinutes)> LoginAsync(LoginRequest request, string? ipAddress);
        Task<(int StatusCode, ApiResponse<RefreshTokenResponse> Response)> RefreshTokenAsync(RefreshTokenRequest request, string? ipAddress);
        Task<(int StatusCode, ApiResponse<bool> Response)> LogoutAsync(string? refreshToken, int userId);
        Task<(int StatusCode, ApiResponse<LoginResponse> Response)> GetCurrentUserProfileAsync(int userId);
        Task<(int StatusCode, ApiResponse<bool> Response)> ChangePasswordAsync(int userId, ChangePasswordRequest request);
    }
}
