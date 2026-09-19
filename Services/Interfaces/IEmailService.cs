using System.Threading.Tasks;

namespace SmartBank.Services.Interfaces
{
    public interface IEmailService
    {
        Task SendOtpAsync(string toEmail, string otpCode);
        Task SendTransferOtpEmailAsync(string toEmail, string recipientAccountNumber, decimal amount, string otpCode);
        Task SendTransferConfirmationEmailAsync(string toEmail, string recipientAccountNumber, decimal amount, string transactionId);
        Task SendEmailVerificationOtpAsync(string toEmail, string otpCode);
        Task SendTransactionAlertAsync(string toEmail, string subject, string message);
    }
}
