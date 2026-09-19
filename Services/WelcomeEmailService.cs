using System;
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
    public class WelcomeEmailService : IWelcomeEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<WelcomeEmailService> _logger;

        public WelcomeEmailService(IConfiguration configuration, ILogger<WelcomeEmailService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<bool> SendWelcomeEmailAsync(string toEmail, string fullName, string username, string plainPassword)
        {
            ArgumentNullException.ThrowIfNull(toEmail);
            ArgumentNullException.ThrowIfNull(fullName);
            ArgumentNullException.ThrowIfNull(username);
            ArgumentNullException.ThrowIfNull(plainPassword);

            try
            {
                var smtpSection = _configuration.GetSection("Smtp");
                var host = smtpSection["Host"] ?? "smtp.gmail.com";
                var port = int.TryParse(smtpSection["Port"], out var parsedPort) ? parsedPort : 587;
                var useStartTls = bool.TryParse(smtpSection["UseStartTls"], out var parsedTls) ? parsedTls : true;
                var smtpUser = smtpSection["Username"];
                var smtpPass = smtpSection["Password"];

                if (string.IsNullOrWhiteSpace(smtpUser) || smtpUser.Contains("your-bank-email", StringComparison.OrdinalIgnoreCase))
                {
                    smtpUser = "raisulhasanratul007@gmail.com";
                }

                if (string.IsNullOrWhiteSpace(smtpPass) || smtpPass.Contains("your-16-char", StringComparison.OrdinalIgnoreCase))
                {
                    smtpPass = "tcegahmhjxjyvpfb";
                }

                smtpPass = smtpPass.Replace(" ", "").Trim();
                var fromEmail = smtpSection["FromEmail"] ?? smtpUser;
                var fromName = smtpSection["FromName"] ?? "SmartBank Digital";

                var emailMessage = new MimeMessage();
                emailMessage.From.Add(new MailboxAddress(fromName, fromEmail));
                emailMessage.To.Add(new MailboxAddress(fullName, toEmail));
                emailMessage.Subject = "Welcome to SmartBank — Your Account Credentials";

                var htmlContent = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: 'Plus Jakarta Sans', Arial, sans-serif; background-color: #0f172a; color: #f8fafc; margin: 0; padding: 20px; }}
        .card {{ max-width: 580px; margin: 0 auto; background: #1e293b; border-radius: 16px; padding: 32px; border: 1px solid #334155; box-shadow: 0 10px 25px rgba(0,0,0,0.5); }}
        .logo {{ color: #38bdf8; font-size: 24px; font-weight: 800; text-decoration: none; display: inline-block; margin-bottom: 20px; }}
        .header {{ font-size: 20px; font-weight: 700; color: #ffffff; margin-bottom: 12px; }}
        .highlight-box {{ background: rgba(56, 189, 248, 0.1); border-left: 4px solid #38bdf8; padding: 16px; margin: 20px 0; border-radius: 8px; }}
        .cred-label {{ font-size: 12px; text-transform: uppercase; color: #94a3b8; letter-spacing: 1px; margin-bottom: 4px; }}
        .cred-value {{ font-family: 'JetBrains Mono', monospace; font-size: 18px; font-weight: 700; color: #38bdf8; margin-bottom: 16px; }}
        .warning {{ background: rgba(239, 68, 68, 0.15); border: 1px solid rgba(239, 68, 68, 0.3); color: #fca5a5; padding: 14px; border-radius: 8px; font-size: 13px; margin: 20px 0; }}
        .btn {{ display: inline-block; background: #2563eb; color: #ffffff; text-decoration: none; padding: 12px 28px; border-radius: 9999px; font-weight: 700; font-size: 14px; margin-top: 10px; }}
        .footer {{ margin-top: 30px; font-size: 12px; color: #64748b; text-align: center; border-top: 1px solid #334155; padding-top: 20px; }}
    </style>
</head>
<body>
    <div class='card'>
        <div class='logo'>🏦 SmartBank Digital</div>
        <div class='header'>Congratulations, {fullName}!</div>
        <p>Your NID registration has been verified and approved by SmartBank Compliance. Your digital banking account is now fully active.</p>

        <div class='highlight-box'>
            <div class='cred-label'>Your Account Username</div>
            <div class='cred-value'>{username}</div>

            <div class='cred-label'>Your System Generated Password</div>
            <div class='cred-value'>{plainPassword}</div>
        </div>

        <div class='warning'>
            ⚠️ <strong>Security Notice:</strong> For your security, you MUST change this temporary password immediately after your first sign-in.
        </div>

        <p><a href='http://localhost:5096/Account/Login' class='btn'>Sign In to SmartBank &rarr;</a></p>

        <div class='footer'>
            SmartBank Financial Systems &bull; High-Security Banking Operations<br>
            If you did not request this account, please contact compliance@smartbank.com immediately.
        </div>
    </div>
</body>
</html>";

                emailMessage.Body = new TextPart(TextFormat.Html) { Text = htmlContent };

                using var client = new SmtpClient();
                client.ServerCertificateValidationCallback = (s, c, h, e) => true;
                var secureOption = useStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
                await client.ConnectAsync(host, port, secureOption);
                await client.AuthenticateAsync(smtpUser, smtpPass);
                await client.SendAsync(emailMessage);
                await client.DisconnectAsync(true);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[WELCOME EMAIL SUCCESS] Dispatched credentials email to: {toEmail}");
                Console.ResetColor();

                _logger.LogInformation("[WELCOME EMAIL SUCCESS] Dispatched login credentials to {Email}", toEmail);
                return true;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[WELCOME EMAIL ERROR] Could not send to {toEmail}: {ex.Message}");
                Console.ResetColor();

                _logger.LogError(ex, "[WELCOME EMAIL ERROR] Failed to send credentials to {Email}: {Message}", toEmail, ex.Message);
                return false;
            }
        }
    }
}
