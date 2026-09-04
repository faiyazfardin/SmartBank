using System;
using System.IO;
using System.Text.Json;

namespace SmartBank.Client.Security
{
    public class SessionData
    {
        public string? Token { get; set; }
        public string? RefreshToken { get; set; }
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public DateTime CreatedAt { get; set; }
        public string AccountNumber { get; set; } = string.Empty;
        public decimal Balance { get; set; }
        public DateTime TokenExpiry { get; set; }
    }

    public class SessionManager
    {
        private static readonly Lazy<SessionManager> _instance = new(() => new SessionManager());
        private readonly object _lock = new();

        public static SessionManager Instance => _instance.Value;

        private static string SessionFilePath
        {
            get
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var folder = Path.Combine(appData, "SmartBank");
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }
                return Path.Combine(folder, "session.json");
            }
        }

        private SessionManager()
        {
            LoadSession();
        }

        public string? Token { get; private set; }
        public string? RefreshToken { get; private set; }
        public int UserId { get; private set; }
        public string Username { get; private set; } = string.Empty;
        public string FullName { get; private set; } = string.Empty;
        public string Role { get; private set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public DateTime CreatedAt { get; set; }
        public string AccountNumber { get; private set; } = string.Empty;

        private decimal _balance;
        public decimal Balance
        {
            get
            {
                lock (_lock) { return _balance; }
            }
            set
            {
                lock (_lock)
                {
                    _balance = value;
                    SaveSession();
                }
            }
        }

        public DateTime TokenExpiry { get; private set; }

        public bool IsAuthenticated
        {
            get
            {
                lock (_lock)
                {
                    return !string.IsNullOrEmpty(Token) && TokenExpiry > DateTime.UtcNow;
                }
            }
        }

        public void LoadSession()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(SessionFilePath))
                    {
                        var json = File.ReadAllText(SessionFilePath);
                        var data = JsonSerializer.Deserialize<SessionData>(json);
                        if (data != null && !string.IsNullOrEmpty(data.Token) && data.TokenExpiry > DateTime.UtcNow)
                        {
                            Token = data.Token;
                            RefreshToken = data.RefreshToken;
                            UserId = data.UserId;
                            Username = data.Username;
                            FullName = data.FullName;
                            Role = data.Role;
                            Email = data.Email;
                            PhoneNumber = data.PhoneNumber;
                            CreatedAt = data.CreatedAt;
                            AccountNumber = data.AccountNumber;
                            _balance = data.Balance;
                            TokenExpiry = data.TokenExpiry;
                        }
                    }
                }
                catch
                {
                    // Fail gracefully
                }
            }
        }

        private void SaveSession()
        {
            try
            {
                if (string.IsNullOrEmpty(Token)) return;

                var data = new SessionData
                {
                    Token = Token,
                    RefreshToken = RefreshToken,
                    UserId = UserId,
                    Username = Username,
                    FullName = FullName,
                    Role = Role,
                    Email = Email,
                    PhoneNumber = PhoneNumber,
                    CreatedAt = CreatedAt,
                    AccountNumber = AccountNumber,
                    Balance = _balance,
                    TokenExpiry = TokenExpiry
                };
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SessionFilePath, json);
            }
            catch
            {
                // Ignore file I/O errors during save
            }
        }

        public void SetSession(string token, string? refreshToken, int userId, string username, string fullName, string role, string accountNumber, decimal balance, int expiresInSeconds)
        {
            lock (_lock)
            {
                Token = token;
                RefreshToken = refreshToken;
                UserId = userId;
                Username = username;
                FullName = fullName;
                Role = role;
                AccountNumber = accountNumber;
                _balance = balance;
                TokenExpiry = DateTime.UtcNow.AddSeconds(expiresInSeconds > 0 ? expiresInSeconds : (30 * 24 * 3600));
                SaveSession();
            }
        }

        public void UpdateToken(string token, string? refreshToken, int expiresInSeconds)
        {
            lock (_lock)
            {
                Token = token;
                if (!string.IsNullOrEmpty(refreshToken))
                {
                    RefreshToken = refreshToken;
                }
                TokenExpiry = DateTime.UtcNow.AddSeconds(expiresInSeconds > 0 ? expiresInSeconds : (30 * 24 * 3600));
                SaveSession();
            }
        }

        public void ClearSession()
        {
            lock (_lock)
            {
                Token = null;
                RefreshToken = null;
                UserId = 0;
                Username = string.Empty;
                FullName = string.Empty;
                Role = string.Empty;
                Email = string.Empty;
                PhoneNumber = null;
                AccountNumber = string.Empty;
                _balance = 0;
                TokenExpiry = DateTime.MinValue;

                try
                {
                    if (File.Exists(SessionFilePath))
                    {
                        File.Delete(SessionFilePath);
                    }
                }
                catch
                {
                }
            }
        }

        public bool IsTokenExpiringSoon()
        {
            lock (_lock)
            {
                if (string.IsNullOrEmpty(Token)) return false;
                return TokenExpiry <= DateTime.UtcNow.AddMinutes(5);
            }
        }
    }
}
