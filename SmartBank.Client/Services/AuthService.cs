using System.Threading.Tasks;
using SmartBank.Client.Models.Auth;
using SmartBank.Client.Models.Common;
using SmartBank.Client.Security;

namespace SmartBank.Client.Services
{
    public class AuthService
    {
        public async Task<ApiResponse<LoginResponse>> LoginAsync(LoginRequest request)
        {
            var response = await ApiClient.PostAsync<ApiResponse<LoginResponse>>("auth/login", request);
            if (response.Success && response.Data != null)
            {
                var d = response.Data;
                SessionManager.Instance.SetSession(
                    d.Token,
                    d.RefreshToken,
                    d.UserId,
                    d.Username,
                    d.FullName,
                    d.Role,
                    d.AccountNumber,
                    d.Balance,
                    d.ExpiresIn
                );
            }
            return response;
        }

        public async Task<ApiResponse<RegisterResponse>> RegisterAsync(RegisterRequest request)
        {
            var response = await ApiClient.PostAsync<ApiResponse<RegisterResponse>>("auth/register", request);
            if (response.Success && response.Data != null)
            {
                var d = response.Data;
                SessionManager.Instance.SetSession(
                    d.Token,
                    d.RefreshToken,
                    d.UserId,
                    d.Username,
                    d.FullName,
                    d.Role,
                    d.AccountNumber,
                    d.Balance,
                    d.ExpiresIn
                );
            }
            return response;
        }

        public async Task<bool> LogoutAsync()
        {
            try
            {
                var refreshToken = SessionManager.Instance.RefreshToken;
                await ApiClient.PostAsync<ApiResponse<bool>>("auth/logout", new { refreshToken });
            }
            catch
            {
                // Proceed with client side cleanup regardless
            }
            finally
            {
                SessionManager.Instance.ClearSession();
            }

            return true;
        }
    }
}
