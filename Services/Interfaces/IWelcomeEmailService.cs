using System.Threading.Tasks;

namespace SmartBank.Services.Interfaces
{
    public interface IWelcomeEmailService
    {
        Task<bool> SendWelcomeEmailAsync(string toEmail, string fullName, string username, string plainPassword);
        Task<bool> SendWelcomeEmailAsync(string toEmail, string fullName, string username, string accountPassword, string vaultPassword);
    }
}
