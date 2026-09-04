namespace SmartBank.DTOs.Auth
{
    public class LoginResponse
    {
        public string Token { get; set; } = string.Empty;
        public string? RefreshToken { get; set; }
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public System.DateTime CreatedAt { get; set; }
        public string AccountNumber { get; set; } = string.Empty;
        public decimal Balance { get; set; } = 0.00m;
        public int ExpiresIn { get; set; } = 3600;
    }
}
