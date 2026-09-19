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

        /// <summary>
        /// Dispatches universal 2-step OTP email for Deposit, Withdraw, Transfer, or Bill Payment.
        /// </summary>
        Task SendUniversalTransactionOtpAsync(
            string toEmail,
            string userName,
            string transactionType,
            decimal amount,
            string? targetInfo,
            string? reference,
            string otpCode);

        /// <summary>
        /// Dispatches confirmation email upon successful completion of a transaction.
        /// </summary>
        Task SendUniversalTransactionConfirmationAsync(
            string toEmail,
            string userName,
            string transactionType,
            decimal amount,
            string trackingId,
            decimal newBalance);
    }
}
