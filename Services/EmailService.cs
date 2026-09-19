using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartBank.Services.Interfaces;

namespace SmartBank.Services
{
    public class EmailService : IEmailService
    {
        private readonly ILogger<EmailService> _logger;
        private readonly string _senderEmail;
        private readonly string _senderName;

        // In-Memory store for local testing/development OTP verification inspection
        public static readonly ConcurrentDictionary<string, string> LatestSentOtps = new();

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
        {
            _logger = logger;
            _senderEmail = configuration["Email:SenderEmail"] ?? "security@smartbank.com";
            _senderName = configuration["Email:SenderName"] ?? "SmartBank Security";
        }

        private string MaskAccountNumber(string accountNumber)
        {
            if (string.IsNullOrEmpty(accountNumber) || accountNumber.Length < 4)
                return "****";
            return $"****{accountNumber.Substring(accountNumber.Length - 4)}";
        }

        public Task SendOtpAsync(string toEmail, string otpCode)
        {
            LatestSentOtps[toEmail.ToLowerInvariant()] = otpCode;
            _logger.LogInformation("[EMAIL DISPATCHED] OTP to {ToEmail}. Verification Code: {OtpCode} (Expires in 5 mins)", toEmail, otpCode);
            return Task.CompletedTask;
        }

        public Task SendTransferOtpEmailAsync(string toEmail, string recipientAccountNumber, decimal amount, string otpCode)
        {
            var maskedRecipient = MaskAccountNumber(recipientAccountNumber);
            LatestSentOtps[toEmail.ToLowerInvariant()] = otpCode;

            _logger.LogInformation(
                "[EMAIL DISPATCHED] Transfer OTP to {ToEmail} for ${Amount:N2} to {RecipientAccount}. Verification Code: {OtpCode} (Expires in 5 mins)",
                toEmail, amount, maskedRecipient, otpCode);

            return Task.CompletedTask;
        }

        public Task SendTransferConfirmationEmailAsync(string toEmail, string recipientAccountNumber, decimal amount, string transactionId)
        {
            var maskedRecipient = MaskAccountNumber(recipientAccountNumber);

            _logger.LogInformation(
                "[EMAIL DISPATCHED] Transfer Confirmation to {ToEmail}. Amount: ${Amount:N2}, Recipient: {RecipientAccount}, TxnId: {TxnId}",
                toEmail, amount, maskedRecipient, transactionId);

            return Task.CompletedTask;
        }

        public Task SendEmailVerificationOtpAsync(string toEmail, string otpCode)
        {
            return SendOtpAsync(toEmail, otpCode);
        }

        public Task SendTransactionAlertAsync(string toEmail, string subject, string message)
        {
            _logger.LogInformation("[EMAIL ALERT DISPATCHED] To: {ToEmail} | Subject: {Subject} | Message: {Message}", toEmail, subject, message);
            return Task.CompletedTask;
        }
    }
}
