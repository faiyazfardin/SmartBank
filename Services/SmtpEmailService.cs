using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using MimeKit.Text;
using SmartBank.Services.Interfaces;

namespace SmartBank.Services
{
    public class SmtpEmailService : IEmailService
    {
        private readonly ILogger<SmtpEmailService> _logger;
        private readonly string _host;
        private readonly int _port;
        private readonly string _username;
        private readonly string _password;
        private readonly string _fromEmail;
        private readonly string _fromName;

        // In-memory inspection cache for testing
        public static readonly ConcurrentDictionary<string, string> LatestSentOtps = new();

        public SmtpEmailService(IConfiguration configuration, ILogger<SmtpEmailService> logger)
        {
            _logger = logger;
            _host = configuration["Smtp:Host"] ?? "smtp.gmail.com";
            _port = int.TryParse(configuration["Smtp:Port"], out var p) ? p : 587;
            
            var user = configuration["Smtp:Username"];
            _username = user ?? string.Empty;

            var pass = configuration["Smtp:Password"];
            _password = (pass ?? string.Empty).Replace(" ", "").Trim();

            _fromEmail = configuration["Smtp:FromEmail"] ?? _username;
            _fromName = configuration["Smtp:FromName"] ?? "SmartBank Digital";
        }

        public async Task SendOtpAsync(string toEmail, string otpCode)
        {
            LatestSentOtps[toEmail.ToLowerInvariant()] = otpCode;

            var subject = $"SmartBank — Your Security Verification Code: {otpCode}";
            var htmlBody = $@"
            <div style=""font-family: 'Segoe UI', Arial, sans-serif; max-width: 520px; margin: 0 auto; background: #0f172a; color: #f8fafc; border-radius: 16px; padding: 32px; border: 1px solid #334155;"">
                <div style=""text-align: center; margin-bottom: 24px;"">
                    <h2 style=""color: #38bdf8; margin: 0; font-size: 24px;"">SmartBank Security</h2>
                    <p style=""color: #94a3b8; font-size: 14px; margin-top: 4px;"">Google OAuth 2-Step Verification</p>
                </div>
                <div style=""background: #1e293b; border-radius: 12px; padding: 24px; text-align: center; border: 1px solid #475569;"">
                    <p style=""color: #cbd5e1; font-size: 14px; margin-bottom: 12px;"">Use the code below to complete your authentication:</p>
                    <div style=""font-size: 36px; font-weight: 800; letter-spacing: 8px; color: #38bdf8; background: #0f172a; padding: 14px 20px; border-radius: 8px; display: inline-block; font-family: monospace; border: 1px dashed #0284c7;"">
                        {otpCode}
                    </div>
                    <p style=""color: #94a3b8; font-size: 12px; margin-top: 16px; margin-bottom: 0;"">
                        ⏰ This code is valid for <strong>5 minutes</strong> and can only be used once.
                    </p>
                </div>
            </div>";

            await SendEmailInternalAsync(toEmail, subject, htmlBody);
        }

        public async Task SendEmailVerificationOtpAsync(string toEmail, string otpCode)
        {
            LatestSentOtps[toEmail.ToLowerInvariant()] = otpCode;

            var subject = $"SmartBank — Confirm Email Verification Code: {otpCode}";
            var htmlBody = $@"
            <div style=""font-family: 'Segoe UI', Arial, sans-serif; max-width: 520px; margin: 0 auto; background: #0f172a; color: #f8fafc; border-radius: 16px; padding: 32px; border: 1px solid #334155;"">
                <div style=""text-align: center; margin-bottom: 24px;"">
                    <h2 style=""color: #38bdf8; margin: 0; font-size: 24px;"">SmartBank Registration</h2>
                    <p style=""color: #94a3b8; font-size: 14px; margin-top: 4px;"">Confirm Your Email Address</p>
                </div>
                <div style=""background: #1e293b; border-radius: 12px; padding: 24px; text-align: center; border: 1px solid #475569;"">
                    <div style=""font-size: 36px; font-weight: 800; letter-spacing: 8px; color: #38bdf8; background: #0f172a; padding: 14px 20px; border-radius: 8px; display: inline-block; font-family: monospace; border: 1px dashed #0284c7;"">
                        {otpCode}
                    </div>
                </div>
            </div>";

            await SendEmailInternalAsync(toEmail, subject, htmlBody);
        }

        public async Task SendTransferOtpEmailAsync(string toEmail, string recipientAccountNumber, decimal amount, string otpCode)
        {
            await SendUniversalTransactionOtpAsync(toEmail, "Valued Customer", "Fund Transfer", amount, $"Account #{recipientAccountNumber}", null, otpCode);
        }

        public async Task SendUniversalTransactionOtpAsync(
            string toEmail,
            string userName,
            string transactionType,
            decimal amount,
            string? targetInfo,
            string? reference,
            string otpCode)
        {
            LatestSentOtps[toEmail.ToLowerInvariant()] = otpCode;

            var subject = $"SmartBank OTP — {transactionType} of {amount:N2} BDT";
            var targetDisplay = string.IsNullOrWhiteSpace(targetInfo) ? "" : $"<p><strong>Target:</strong> {targetInfo}</p>";
            var refDisplay = string.IsNullOrWhiteSpace(reference) ? "" : $"<p><strong>Reference:</strong> {reference}</p>";

            var htmlBody = $@"
            <div style=""font-family: 'Segoe UI', Arial, sans-serif; max-width: 520px; margin: 0 auto; background: #0f172a; color: #f8fafc; border-radius: 16px; padding: 32px; border: 1px solid #334155;"">
                <div style=""text-align: center; margin-bottom: 24px;"">
                    <h2 style=""color: #38bdf8; margin: 0; font-size: 24px;"">SmartBank Security OTP</h2>
                </div>
                <div style=""background: #1e293b; border-radius: 12px; padding: 24px; border: 1px solid #475569;"">
                    <p>Hello {userName},</p>
                    <p>Your OTP for the following transaction is:</p>
                    <ul style=""list-style: none; padding: 0; color: #cbd5e1; font-size: 14px;"">
                        <li><strong>Transaction Type:</strong> {transactionType}</li>
                        <li><strong>Amount:</strong> ৳{amount:N2} BDT</li>
                        {(string.IsNullOrWhiteSpace(targetInfo) ? "" : $"<li><strong>Target:</strong> {targetInfo}</li>")}
                        {(string.IsNullOrWhiteSpace(reference) ? "" : $"<li><strong>Reference:</strong> {reference}</li>")}
                    </ul>
                    <div style=""text-align: center; margin: 20px 0;"">
                        <div style=""font-size: 36px; font-weight: 800; letter-spacing: 8px; color: #38bdf8; background: #0f172a; padding: 14px 20px; border-radius: 8px; display: inline-block; font-family: monospace; border: 1px dashed #0284c7;"">
                            🔐 {otpCode}
                        </div>
                    </div>
                    <p style=""color: #f87171; font-size: 12px; margin-top: 16px; text-align: center;"">
                        ⏰ This OTP is valid for <strong>2 minutes</strong> (120 seconds) and can be used only once.<br/>
                        Do not share this code with anyone.
                    </p>
                    <p style=""color: #94a3b8; font-size: 12px; margin-top: 12px; text-align: center;"">
                        If you did not initiate this transaction, contact SmartBank Security support immediately.
                    </p>
                </div>
            </div>";

            await SendEmailInternalAsync(toEmail, subject, htmlBody);
        }

        public async Task SendTransferConfirmationEmailAsync(string toEmail, string recipientAccountNumber, decimal amount, string transactionId)
        {
            await SendUniversalTransactionConfirmationAsync(toEmail, "Valued Customer", "Fund Transfer", amount, transactionId, 0m);
        }

        public async Task SendUniversalTransactionConfirmationAsync(
            string toEmail,
            string userName,
            string transactionType,
            decimal amount,
            string trackingId,
            decimal newBalance)
        {
            var subject = $"SmartBank — {transactionType} Successful: ৳{amount:N2}";
            var htmlBody = $@"
            <div style=""font-family: 'Segoe UI', Arial, sans-serif; max-width: 520px; margin: 0 auto; background: #0f172a; color: #f8fafc; border-radius: 16px; padding: 32px; border: 1px solid #334155;"">
                <div style=""text-align: center; margin-bottom: 24px;"">
                    <h2 style=""color: #34d399; margin: 0; font-size: 24px;"">{transactionType} Completed</h2>
                </div>
                <div style=""background: #1e293b; border-radius: 12px; padding: 24px; border: 1px solid #475569;"">
                    <p style=""color: #cbd5e1; font-size: 14px;"">Your <strong>{transactionType}</strong> of <strong style=""color: #34d399;"">৳{amount:N2}</strong> was successfully processed.</p>
                    <p style=""color: #cbd5e1; font-size: 14px;"">Updated Balance: <strong style=""color: #38bdf8;"">৳{newBalance:N2}</strong></p>
                    <p style=""color: #94a3b8; font-size: 12px; margin-bottom: 0;"">Tracking ID: <strong>{trackingId}</strong></p>
                </div>
            </div>";

            await SendEmailInternalAsync(toEmail, subject, htmlBody);
        }

        public async Task SendTransactionAlertAsync(string toEmail, string subject, string message)
        {
            var html = $"<div style='font-family: Arial; padding: 20px; background: #0f172a; color: white;'><h3>{subject}</h3><p>{message}</p></div>";
            await SendEmailInternalAsync(toEmail, subject, html);
        }

        private async Task SendEmailInternalAsync(string toEmail, string subject, string htmlContent)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_username) || string.IsNullOrWhiteSpace(_password))
                {
                    _logger.LogInformation("[LOCAL OTP DISPATCH] To: {Email} | Subject: {Subject}", toEmail, subject);
                    return;
                }

                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(_fromName, _fromEmail));
                message.To.Add(new MailboxAddress(toEmail, toEmail));
                message.Subject = subject;
                message.Body = new TextPart(TextFormat.Html) { Text = htmlContent };

                using var client = new SmtpClient();
                client.ServerCertificateValidationCallback = (s, c, h, e) => true;
                await client.ConnectAsync(_host, _port, SecureSocketOptions.StartTls);
                await client.AuthenticateAsync(_username, _password);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);

                _logger.LogInformation("Email dispatched successfully to {Email}", toEmail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {Email} via SMTP {Host}:{Port}", toEmail, _host, _port);
            }
        }
    }
}
