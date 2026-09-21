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

        public Task<bool> SendWelcomeEmailAsync(string toEmail, string fullName, string username, string plainPassword)
        {
            return SendWelcomeEmailAsync(toEmail, fullName, username, plainPassword, plainPassword);
        }

        public async Task<bool> SendWelcomeEmailAsync(string toEmail, string fullName, string username, string accountPassword, string vaultPassword)
        {
            ArgumentNullException.ThrowIfNull(toEmail);
            ArgumentNullException.ThrowIfNull(fullName);
            ArgumentNullException.ThrowIfNull(username);
            ArgumentNullException.ThrowIfNull(accountPassword);
            ArgumentNullException.ThrowIfNull(vaultPassword);

            try
            {
                var smtpSection = _configuration.GetSection("Smtp");
                var host = smtpSection["Host"] ?? "smtp.gmail.com";
                var port = int.TryParse(smtpSection["Port"], out var parsedPort) ? parsedPort : 587;
                var useStartTls = bool.TryParse(smtpSection["UseStartTls"], out var parsedTls) ? parsedTls : true;
                var smtpUser = smtpSection["Username"] ?? string.Empty;
                var smtpPass = (smtpSection["Password"] ?? string.Empty).Replace(" ", "").Trim();
                var fromEmail = smtpSection["FromEmail"] ?? smtpUser;
                var fromName = smtpSection["FromName"] ?? "SmartBank Digital";

                if (string.IsNullOrWhiteSpace(smtpUser) || string.IsNullOrWhiteSpace(smtpPass) || smtpUser.StartsWith("YOUR_") || smtpPass.StartsWith("YOUR_"))
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"[WELCOME EMAIL DEV MODE] Simulated email to: {toEmail} | Username: {username} | Account Pass: {accountPassword} | Vault Pass: {vaultPassword}");
                    Console.ResetColor();

                    _logger.LogInformation("[WELCOME EMAIL DEV MODE] Simulated credentials email to {Email}", toEmail);
                    return true;
                }

                var emailMessage = new MimeMessage();
                emailMessage.From.Add(new MailboxAddress(fromName, fromEmail));
                emailMessage.To.Add(new MailboxAddress(fullName, toEmail));
                emailMessage.Subject = "Welcome to SmartBank — Your Account & Vault Credentials";

                var htmlContent = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: 'Plus Jakarta Sans', Arial, sans-serif; background-color: #0f172a; color: #f8fafc; margin: 0; padding: 20px; }}
        .card {{ max-width: 600px; margin: 0 auto; background: #1e293b; border-radius: 16px; padding: 32px; border: 1px solid #334155; box-shadow: 0 10px 25px rgba(0,0,0,0.5); }}
        .logo {{ color: #38bdf8; font-size: 24px; font-weight: 800; text-decoration: none; display: inline-block; margin-bottom: 20px; }}
        .header {{ font-size: 20px; font-weight: 700; color: #ffffff; margin-bottom: 12px; }}
        .highlight-box {{ background: rgba(56, 189, 248, 0.08); border: 1px solid rgba(56, 189, 248, 0.25); padding: 20px; margin: 20px 0; border-radius: 12px; }}
        .cred-item {{ margin-bottom: 16px; padding-bottom: 14px; border-bottom: 1px solid rgba(255, 255, 255, 0.08); }}
        .cred-item:last-child {{ margin-bottom: 0; padding-bottom: 0; border-bottom: none; }}
        .cred-label {{ font-size: 11px; text-transform: uppercase; color: #94a3b8; letter-spacing: 1px; font-weight: 700; margin-bottom: 4px; }}
        .cred-desc {{ font-size: 12px; color: #cbd5e1; margin-bottom: 6px; }}
        .cred-value {{ font-family: 'JetBrains Mono', monospace; font-size: 18px; font-weight: 700; color: #38bdf8; background: rgba(15, 23, 42, 0.8); padding: 8px 12px; border-radius: 6px; display: inline-block; }}
        .warning {{ background: rgba(239, 68, 68, 0.15); border: 1px solid rgba(239, 68, 68, 0.3); color: #fca5a5; padding: 16px; border-radius: 10px; font-size: 13px; margin: 20px 0; }}
        .rules-list {{ margin: 8px 0 0 16px; padding: 0; font-size: 12px; color: #fecaca; }}
        .rules-list li {{ margin-bottom: 4px; }}
        .btn {{ display: inline-block; background: linear-gradient(135deg, #0284c7 0%, #2563eb 100%); color: #ffffff !important; text-decoration: none; padding: 12px 28px; border-radius: 9999px; font-weight: 700; font-size: 14px; margin-top: 10px; }}
        .footer {{ margin-top: 30px; font-size: 12px; color: #64748b; text-align: center; border-top: 1px solid #334155; padding-top: 20px; }}
    </style>
</head>
<body>
    <div class='card'>
        <div class='logo'>🏦 SmartBank Digital</div>
        <div class='header'>Congratulations, {fullName}!</div>
        <p>Your NID registration has been verified and approved by SmartBank Compliance. Your high-security digital banking profile is now fully active.</p>

        <p>SmartBank uses a <strong>Dual-Password Security Architecture</strong> to protect your finances. Below are your temporary system-generated passwords:</p>

        <div class='highlight-box'>
            <div class='cred-item'>
                <div class='cred-label'>1. Account Username</div>
                <div class='cred-value'>{username}</div>
            </div>

            <div class='cred-item'>
                <div class='cred-label'>2. Account Login Password</div>
                <div class='cred-desc'>Used exclusively to sign in to your banking dashboard and initiate operations.</div>
                <div class='cred-value'>{accountPassword}</div>
            </div>

            <div class='cred-item'>
                <div class='cred-label'>3. Security Vault Password</div>
                <div class='cred-desc'>Used to unlock the encrypted Vault where your Account Balance, Card details, and Full Transaction History are stored.</div>
                <div class='cred-value'>{vaultPassword}</div>
            </div>
        </div>

        <div class='warning'>
            ⚠️ <strong>Mandatory First-Login Policy:</strong>
            <p style='margin: 6px 0 0 0;'>Upon your first sign-in, you will be <strong>required to change BOTH passwords</strong> before accessing your dashboard or vault.</p>
            <ul class='rules-list'>
                <li>Minimum length: 8 characters</li>
                <li>Must contain uppercase, lowercase, digit, and special character (!@#$%^&*)</li>
                <li>Vault Password must be different from Account Password</li>
            </ul>
        </div>

        <p style='text-align: center; margin-top: 24px;'>
            <a href='http://localhost:5096/Account/Login' class='btn'>Sign In & Activate Profile &rarr;</a>
        </p>

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
                Console.WriteLine($"[WELCOME EMAIL SUCCESS] Dispatched dual credentials email to: {toEmail}");
                Console.ResetColor();

                _logger.LogInformation("[WELCOME EMAIL SUCCESS] Dispatched credentials to {Email}", toEmail);
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
