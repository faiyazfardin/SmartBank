using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SmartBank.Data;
using SmartBank.DTOs.Auth;
using SmartBank.DTOs.Common;
using SmartBank.Entities;
using SmartBank.Security;
using SmartBank.Services.Interfaces;

namespace SmartBank.Services
{
    public class AuthService : IAuthService
    {
        private readonly SmartBankDbContext _context;
        private readonly IJwtService _jwtService;
        private readonly IRateLimitService _rateLimitService;
        private readonly IRefreshTokenService _refreshTokenService;
        private readonly int _lockoutMinutes;
        private readonly int _maxFailedAttempts;

        public AuthService(
            SmartBankDbContext context,
            IJwtService jwtService,
            IRateLimitService rateLimitService,
            IRefreshTokenService refreshTokenService,
            IConfiguration configuration)
        {
            _context = context;
            _jwtService = jwtService;
            _rateLimitService = rateLimitService;
            _refreshTokenService = refreshTokenService;
            _lockoutMinutes = int.TryParse(configuration["Security:LockoutMinutes"], out var lockout) ? lockout : 15;
            _maxFailedAttempts = int.TryParse(configuration["Security:MaxLoginAttempts"], out var maxAttempts) ? maxAttempts : 5;
        }

        private async Task<string> GenerateUnique12DigitAccountNumberAsync()
        {
            while (true)
            {
                // Generate a random 12-digit number (100000000000 to 999999999999)
                var bytes = new byte[8];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(bytes);
                }
                var rawValue = BitConverter.ToUInt64(bytes, 0);
                var number = (100000000000UL + (rawValue % 900000000000UL)).ToString();

                var exists = await _context.Accounts.AnyAsync(a => a.AccountNumber == number);
                if (!exists)
                {
                    return number;
                }
            }
        }

        public async Task<(int StatusCode, ApiResponse<RegisterResponse> Response)> RegisterAsync(RegisterRequest request, string? ipAddress)
        {
            var normalizedUsername = request.Username.Trim().ToLowerInvariant();
            var normalizedEmail = request.Email.Trim().ToLowerInvariant();

            // Check duplicate username
            var usernameExists = await _context.Users.AnyAsync(u => u.Username.ToLower() == normalizedUsername);
            if (usernameExists)
            {
                return (409, ApiResponse<RegisterResponse>.FailureResponse(
                    "Registration failed",
                    new List<string> { "Username is already taken" }));
            }

            // Check duplicate email
            var emailExists = await _context.Users.AnyAsync(u => u.Email.ToLower() == normalizedEmail);
            if (emailExists)
            {
                return (409, ApiResponse<RegisterResponse>.FailureResponse(
                    "Registration failed",
                    new List<string> { "Email is already registered" }));
            }

            var passwordHash = PasswordHasher.HashPassword(request.Password);
            var accountNumber = await GenerateUnique12DigitAccountNumberAsync();

            var now = DateTime.UtcNow;
            var user = new User
            {
                FullName = request.FullName.Trim(),
                Email = normalizedEmail,
                PhoneNumber = request.PhoneNumber?.Trim(),
                Username = normalizedUsername,
                PasswordHash = passwordHash,
                Role = "Customer",
                Status = "Active",
                FailedLoginCount = 0,
                LockedUntil = null,
                CreatedAt = now,
                UpdatedAt = now
            };

            var initialBalance = 2500.00m; // Welcome promotional bonus for new members
            var account = new Account
            {
                AccountNumber = accountNumber,
                Balance = initialBalance,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            };

            user.Accounts.Add(account);

            // Execute within transaction
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                if (initialBalance > 0)
                {
                    var welcomeTx = new Transaction
                    {
                        AccountId = account.Id,
                        Type = TransactionType.Deposit,
                        Amount = initialBalance,
                        Timestamp = now
                    };
                    _context.Transactions.Add(welcomeTx);
                    await _context.SaveChangesAsync();
                }

                var token = _jwtService.GenerateToken(user, accountNumber);
                var (rawRefreshToken, _) = await _refreshTokenService.GenerateRefreshTokenAsync(user.Id, ipAddress);

                await transaction.CommitAsync();

                var responseData = new RegisterResponse
                {
                    Token = token,
                    RefreshToken = rawRefreshToken,
                    UserId = user.Id,
                    Username = user.Username,
                    FullName = user.FullName,
                    Role = user.Role,
                    AccountNumber = accountNumber,
                    Balance = account.Balance,
                    ExpiresIn = _jwtService.GetExpiryMinutes() * 60
                };

                return (201, ApiResponse<RegisterResponse>.SuccessResponse(responseData, "Registration successful"));
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return (500, ApiResponse<RegisterResponse>.FailureResponse("An unexpected error occurred during registration", ex.Message));
            }
        }

        public async Task<(int StatusCode, ApiResponse<LoginResponse> Response, int? RetryAfterMinutes)> LoginAsync(LoginRequest request, string? ipAddress)
        {
            var clientIp = ipAddress ?? "127.0.0.1";

            // 1. Check IP rate limit
            if (_rateLimitService.IsRateLimited(clientIp, out var retryAfter))
            {
                return (429, ApiResponse<LoginResponse>.FailureResponse(
                    "Too many login attempts",
                    new List<string> { $"Please wait {retryAfter} minutes before trying again" }), retryAfter);
            }

            var normalizedUsername = request.Username.Trim().ToLowerInvariant();

            // 2. Find user
            var user = await _context.Users
                .Include(u => u.Accounts)
                .FirstOrDefaultAsync(u => u.Username.ToLower() == normalizedUsername);

            if (user == null)
            {
                _rateLimitService.RecordFailedAttempt(clientIp);
                return (401, ApiResponse<LoginResponse>.FailureResponse(
                    "Invalid username or password",
                    new List<string> { "The username or password is incorrect" }), null);
            }

            if (user.Status != null && user.Status.Equals("Suspended", StringComparison.OrdinalIgnoreCase))
            {
                return (403, ApiResponse<LoginResponse>.FailureResponse(
                    "Account suspended",
                    new List<string> { "Your account has been suspended by Bank Administration & Compliance. Please contact executive support." }), null);
            }

            // 3. Check account lockout
            if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTime.UtcNow)
            {
                var remainingMinutes = (int)Math.Ceiling((user.LockedUntil.Value - DateTime.UtcNow).TotalMinutes);
                if (remainingMinutes <= 0) remainingMinutes = 1;

                return (403, ApiResponse<LoginResponse>.FailureResponse(
                    "Account temporarily locked",
                    new List<string> { $"Too many failed attempts. Please try again in {remainingMinutes} minutes" }), remainingMinutes);
            }

            // 4. Verify password
            var isPasswordValid = PasswordHasher.VerifyPassword(request.Password, user.PasswordHash);
            if (!isPasswordValid)
            {
                _rateLimitService.RecordFailedAttempt(clientIp);

                user.FailedLoginCount++;
                user.UpdatedAt = DateTime.UtcNow;

                if (user.FailedLoginCount >= _maxFailedAttempts)
                {
                    user.LockedUntil = DateTime.UtcNow.AddMinutes(_lockoutMinutes);
                    await _context.SaveChangesAsync();

                    return (403, ApiResponse<LoginResponse>.FailureResponse(
                        "Account temporarily locked",
                        new List<string> { $"Too many failed attempts. Please try again in {_lockoutMinutes} minutes" }), _lockoutMinutes);
                }

                await _context.SaveChangesAsync();
                return (401, ApiResponse<LoginResponse>.FailureResponse(
                    "Invalid username or password",
                    new List<string> { "The username or password is incorrect" }), null);
            }

            // 5. Success: Reset failed attempts & lockout
            user.FailedLoginCount = 0;
            user.LockedUntil = null;
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _rateLimitService.ResetAttempts(clientIp);

            var primaryAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.UserId == user.Id);
            var accountNumber = primaryAccount?.AccountNumber ?? string.Empty;
            var balance = primaryAccount?.Balance ?? 0.00m;

            var token = _jwtService.GenerateToken(user, accountNumber);
            var (rawRefreshToken, _) = await _refreshTokenService.GenerateRefreshTokenAsync(user.Id, clientIp);

            var responseData = new LoginResponse
            {
                Token = token,
                RefreshToken = rawRefreshToken,
                UserId = user.Id,
                Username = user.Username,
                FullName = user.FullName,
                Role = user.Role,
                AccountNumber = accountNumber,
                Balance = balance,
                ExpiresIn = _jwtService.GetExpiryMinutes() * 60
            };

            return (200, ApiResponse<LoginResponse>.SuccessResponse(responseData, "Login successful"), null);
        }

        public async Task<(int StatusCode, ApiResponse<RefreshTokenResponse> Response)> RefreshTokenAsync(RefreshTokenRequest request, string? ipAddress)
        {
            var principal = _jwtService.GetPrincipalFromExpiredToken(request.Token);
            if (principal == null)
            {
                return (401, ApiResponse<RefreshTokenResponse>.FailureResponse("Invalid access token", "Could not validate token claims"));
            }

            var userIdClaim = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? principal.FindFirst("sub")?.Value;

            if (!int.TryParse(userIdClaim, out var userId))
            {
                return (401, ApiResponse<RefreshTokenResponse>.FailureResponse("Invalid token payload", "User ID not found in token"));
            }

            var user = await _context.Users
                .Include(u => u.Accounts)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null || user.Status != "Active")
            {
                return (401, ApiResponse<RefreshTokenResponse>.FailureResponse("Invalid user", "User is inactive or no longer exists"));
            }

            var (rotated, newRawRefreshToken) = await _refreshTokenService.ValidateAndRotateRefreshTokenAsync(request.RefreshToken, userId, ipAddress);
            if (rotated == null || string.IsNullOrEmpty(newRawRefreshToken))
            {
                return (401, ApiResponse<RefreshTokenResponse>.FailureResponse("Invalid or expired refresh token", "Refresh token validation failed"));
            }

            var primaryAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.UserId == user.Id);
            var newToken = _jwtService.GenerateToken(user, primaryAccount?.AccountNumber);

            var responseData = new RefreshTokenResponse
            {
                Token = newToken,
                RefreshToken = newRawRefreshToken,
                ExpiresIn = _jwtService.GetExpiryMinutes() * 60
            };

            return (200, ApiResponse<RefreshTokenResponse>.SuccessResponse(responseData, "Token refreshed"));
        }

        public async Task<(int StatusCode, ApiResponse<bool> Response)> LogoutAsync(string? refreshToken, int userId)
        {
            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                await _refreshTokenService.RevokeRefreshTokenAsync(refreshToken, userId);
            }

            return (200, ApiResponse<bool>.SuccessResponse(true, "Logged out successfully"));
        }

        public async Task<(int StatusCode, ApiResponse<LoginResponse> Response)> GetCurrentUserProfileAsync(int userId)
        {
            var user = await _context.Users
                .Include(u => u.Accounts)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                return (404, ApiResponse<LoginResponse>.FailureResponse("User not found"));
            }

            var primaryAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.UserId == user.Id);
            var responseData = new LoginResponse
            {
                Token = string.Empty,
                RefreshToken = null,
                UserId = user.Id,
                Username = user.Username,
                FullName = user.FullName,
                Role = user.Role,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                CreatedAt = user.CreatedAt,
                AccountNumber = primaryAccount?.AccountNumber ?? string.Empty,
                Balance = primaryAccount?.Balance ?? 0.00m,
                ExpiresIn = _jwtService.GetExpiryMinutes() * 60
            };

            return (200, ApiResponse<LoginResponse>.SuccessResponse(responseData));
        }

        public async Task<(int StatusCode, ApiResponse<LoginResponse> Response)> UpdateProfileAsync(int userId, UpdateProfileRequest request)
        {
            var user = await _context.Users
                .Include(u => u.Accounts)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                return (404, ApiResponse<LoginResponse>.FailureResponse("User not found"));
            }

            var normalizedEmail = request.Email.Trim().ToLowerInvariant();
            var emailExists = await _context.Users.AnyAsync(u => u.Email.ToLower() == normalizedEmail && u.Id != userId);
            if (emailExists)
            {
                return (409, ApiResponse<LoginResponse>.FailureResponse("Email is already in use by another account"));
            }

            user.FullName = request.FullName.Trim();
            user.Email = normalizedEmail;
            user.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            var primaryAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.UserId == user.Id);
            var responseData = new LoginResponse
            {
                Token = string.Empty,
                RefreshToken = null,
                UserId = user.Id,
                Username = user.Username,
                FullName = user.FullName,
                Role = user.Role,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                CreatedAt = user.CreatedAt,
                AccountNumber = primaryAccount?.AccountNumber ?? string.Empty,
                Balance = primaryAccount?.Balance ?? 0.00m,
                ExpiresIn = _jwtService.GetExpiryMinutes() * 60
            };

            return (200, ApiResponse<LoginResponse>.SuccessResponse(responseData, "Profile updated successfully"));
        }

        public async Task<(int StatusCode, ApiResponse<bool> Response)> ChangePasswordAsync(int userId, ChangePasswordRequest request)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                return (404, ApiResponse<bool>.FailureResponse("User not found"));
            }

            if (!PasswordHasher.VerifyPassword(request.CurrentPassword, user.PasswordHash))
            {
                return (400, ApiResponse<bool>.FailureResponse("Incorrect current password"));
            }

            if (request.CurrentPassword == request.NewPassword)
            {
                return (400, ApiResponse<bool>.FailureResponse("New password must be different from current password"));
            }

            user.PasswordHash = PasswordHasher.HashPassword(request.NewPassword);
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return (200, ApiResponse<bool>.SuccessResponse(true, "Password changed successfully"));
        }
    }
}
