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
        private readonly IOtpService _otpService;
        private readonly IEmailService _emailService;
        private readonly int _lockoutMinutes;
        private readonly int _maxFailedAttempts;

        public AuthService(
            SmartBankDbContext context,
            IJwtService jwtService,
            IRateLimitService rateLimitService,
            IRefreshTokenService refreshTokenService,
            IOtpService otpService,
            IEmailService emailService,
            IConfiguration configuration)
        {
            _context = context;
            _jwtService = jwtService;
            _rateLimitService = rateLimitService;
            _refreshTokenService = refreshTokenService;
            _otpService = otpService;
            _emailService = emailService;
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

            // Check duplicate username (case-insensitive)
            var usernameExists = await _context.Users.AnyAsync(u => u.Username.ToLower() == normalizedUsername);
            if (usernameExists)
            {
                return (409, ApiResponse<RegisterResponse>.FailureResponse(
                    "Registration failed",
                    new List<string> { "This username is already taken. Please choose a different username." }));
            }

            // Check duplicate email (case-insensitive)
            var emailExists = await _context.Users.AnyAsync(u => u.Email.ToLower() == normalizedEmail);
            if (emailExists)
            {
                return (409, ApiResponse<RegisterResponse>.FailureResponse(
                    "Registration failed",
                    new List<string> { "An account with this email address already exists. Please sign in or use another email." }));
            }

            if (string.IsNullOrWhiteSpace(request.Password))
            {
                request.Password = SmartBank.Helpers.PasswordGeneratorHelper.GenerateSecurePassword(10);
            }

            var passwordHash = PasswordHasher.HashPassword(request.Password);
            var accountNumber = await GenerateUnique12DigitAccountNumberAsync();

            var now = DateTime.UtcNow;
            var user = new User
            {
                FullName = request.FullName.Trim(),
                Email = normalizedEmail,
                PhoneNumber = request.PhoneNumber?.Trim(),
                NidNumber = request.NidNumber?.Trim(),
                Username = normalizedUsername,
                PasswordHash = passwordHash,
                MustChangePasswordOnNextLogin = true,
                TemporaryPasswordIssuedAtUtc = now,
                Role = "Customer",
                Status = "Pending",
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
                IsActive = false, // Activated upon admin approval
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
            catch (DbUpdateException dbEx)
            {
                await transaction.RollbackAsync();
                var innerMsg = dbEx.InnerException?.Message ?? dbEx.Message;

                if (innerMsg.Contains("IX_Users_Email", StringComparison.OrdinalIgnoreCase) ||
                    innerMsg.Contains("email", StringComparison.OrdinalIgnoreCase))
                {
                    return (409, ApiResponse<RegisterResponse>.FailureResponse(
                        "Registration failed",
                        new List<string> { "An account with this email address already exists. Please sign in or use another email." }));
                }

                if (innerMsg.Contains("IX_Users_Username", StringComparison.OrdinalIgnoreCase) ||
                    innerMsg.Contains("username", StringComparison.OrdinalIgnoreCase))
                {
                    return (409, ApiResponse<RegisterResponse>.FailureResponse(
                        "Registration failed",
                        new List<string> { "This username is already taken. Please choose a different username." }));
                }

                return (500, ApiResponse<RegisterResponse>.FailureResponse(
                    "Registration failed",
                    new List<string> { "Database constraint issue: " + innerMsg }));
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                var message = ex.InnerException?.Message ?? ex.Message;
                return (500, ApiResponse<RegisterResponse>.FailureResponse(
                    "An unexpected error occurred during registration",
                    new List<string> { message }));
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

            if (user.Status != null && user.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
            {
                return (403, ApiResponse<LoginResponse>.FailureResponse(
                    "Account Pending Verification",
                    new List<string> { "Your account registration is currently pending administrator verification (NID Review). You will be able to log in once an Admin approves your request." }), null);
            }

            if (user.Status != null && user.Status.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
            {
                return (403, ApiResponse<LoginResponse>.FailureResponse(
                    "Registration Rejected",
                    new List<string> { "Your account registration request was rejected by Bank Administration. Please contact support." }), null);
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

            var (isValid, errorMessage) = SmartBank.Security.PasswordValidator.Validate(request.NewPassword);
            if (!isValid)
            {
                return (400, ApiResponse<bool>.FailureResponse("Validation error", errorMessage));
            }

            user.PasswordHash = PasswordHasher.HashPassword(request.NewPassword);
            user.MustChangePasswordOnNextLogin = false;
            user.TemporaryPasswordIssuedAtUtc = null;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return (200, ApiResponse<bool>.SuccessResponse(true, "Password changed successfully"));
        }

        public async Task<GoogleLoginResult> ProcessGoogleLoginAsync(string googleSubjectId, string email, string fullName, string? ipAddress)
        {
            if (string.IsNullOrWhiteSpace(googleSubjectId) || string.IsNullOrWhiteSpace(email))
            {
                return new GoogleLoginResult
                {
                    Status = GoogleAuthStatus.Failed,
                    ErrorMessage = "Missing verified identity claims from Google."
                };
            }

            var cleanSubject = googleSubjectId.Trim();
            var cleanEmail = email.Trim().ToLowerInvariant();

            // 1. Check if an ExternalLogin exists for this Google subject
            var externalLogin = await _context.ExternalLogins
                .Include(e => e.User)
                    .ThenInclude(u => u.Accounts)
                .FirstOrDefaultAsync(e => e.Provider == "Google" && e.ProviderUserId == cleanSubject);

            if (externalLogin != null)
            {
                var user = externalLogin.User;
                var now = DateTime.UtcNow;

                if (user.Status != null && user.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
                {
                    return new GoogleLoginResult
                    {
                        Status = GoogleAuthStatus.Pending,
                        ErrorMessage = "Your account registration is currently pending administrator verification (NID Review). You will be able to log in once an Admin approves your request."
                    };
                }

                if (user.Status != null && user.Status.Equals("Suspended", StringComparison.OrdinalIgnoreCase))
                {
                    return new GoogleLoginResult
                    {
                        Status = GoogleAuthStatus.Suspended,
                        ErrorMessage = "This account has been administratively suspended. Access is prohibited."
                    };
                }

                // Update last login
                externalLogin.LastLoginAt = now;
                user.UpdatedAt = now;
                await _context.SaveChangesAsync();

                var primaryAccount = user.Accounts.FirstOrDefault();
                var token = _jwtService.GenerateToken(user, primaryAccount?.AccountNumber);
                var (rawRefreshToken, _) = await _refreshTokenService.GenerateRefreshTokenAsync(user.Id, ipAddress);

                var loginResponse = new LoginResponse
                {
                    Token = token,
                    RefreshToken = rawRefreshToken,
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

                return new GoogleLoginResult
                {
                    Status = GoogleAuthStatus.Authenticated,
                    LoginData = loginResponse
                };
            }

            // 2. Not linked: Check if an existing SmartBank user has the same email
            var existingUserWithEmail = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == cleanEmail);
            if (existingUserWithEmail != null)
            {
                if (existingUserWithEmail.Status != null && existingUserWithEmail.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
                {
                    return new GoogleLoginResult
                    {
                        Status = GoogleAuthStatus.Pending,
                        ErrorMessage = $"Registration submitted successfully! Your account with NID: {existingUserWithEmail.NidNumber ?? "submitted"} is pending administrator verification. Once an Admin approves your account, you will be able to sign in."
                    };
                }

                if (existingUserWithEmail.Status != null && existingUserWithEmail.Status.Equals("Suspended", StringComparison.OrdinalIgnoreCase))
                {
                    return new GoogleLoginResult
                    {
                        Status = GoogleAuthStatus.Suspended,
                        ErrorMessage = "This account has been administratively suspended. Access is prohibited."
                    };
                }

                // REQUIRE EXPLICIT ACCOUNT LINKING TO PREVENT ACCOUNT TAKEOVER
                return new GoogleLoginResult
                {
                    Status = GoogleAuthStatus.RequiresLinking,
                    GoogleSubjectId = cleanSubject,
                    Email = cleanEmail,
                    FullName = fullName
                };
            }

            // 3. New Google user -> Requires completing registration with required banking info
            return new GoogleLoginResult
            {
                Status = GoogleAuthStatus.RequiresRegistration,
                GoogleSubjectId = cleanSubject,
                Email = cleanEmail,
                FullName = fullName
            };
        }

        public async Task<(int StatusCode, ApiResponse<LoginResponse> Response)> LinkGoogleAccountAsync(LinkGoogleRequest request, string? ipAddress)
        {
            var cleanEmail = request.Email.Trim().ToLowerInvariant();
            var user = await _context.Users
                .Include(u => u.Accounts)
                .Include(u => u.ExternalLogins)
                .FirstOrDefaultAsync(u => u.Email.ToLower() == cleanEmail);

            if (user == null)
            {
                return (404, ApiResponse<LoginResponse>.FailureResponse("No SmartBank account found with this email address."));
            }

            if (!PasswordHasher.VerifyPassword(request.Password, user.PasswordHash))
            {
                return (401, ApiResponse<LoginResponse>.FailureResponse("Incorrect SmartBank password. Unable to link Google account."));
            }

            if (user.Status != null && user.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
            {
                return (403, ApiResponse<LoginResponse>.FailureResponse("Your account registration is currently pending administrator verification (NID Review)."));
            }

            if (user.Status != null && user.Status.Equals("Suspended", StringComparison.OrdinalIgnoreCase))
            {
                return (403, ApiResponse<LoginResponse>.FailureResponse("This account has been administratively suspended. Access is prohibited."));
            }

            // Check if already linked
            var alreadyLinked = user.ExternalLogins.Any(e => e.Provider == "Google" && e.ProviderUserId == request.GoogleSubjectId);
            if (!alreadyLinked)
            {
                var externalLogin = new ExternalLogin
                {
                    UserId = user.Id,
                    Provider = "Google",
                    ProviderUserId = request.GoogleSubjectId.Trim(),
                    Email = cleanEmail,
                    CreatedAt = DateTime.UtcNow,
                    LastLoginAt = DateTime.UtcNow
                };

                _context.ExternalLogins.Add(externalLogin);
            }

            user.IsEmailVerified = true;
            user.EmailVerifiedAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            var primaryAccount = user.Accounts.FirstOrDefault();
            var token = _jwtService.GenerateToken(user, primaryAccount?.AccountNumber);
            var (rawRefreshToken, _) = await _refreshTokenService.GenerateRefreshTokenAsync(user.Id, ipAddress);

            var loginResponse = new LoginResponse
            {
                Token = token,
                RefreshToken = rawRefreshToken,
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

            return (200, ApiResponse<LoginResponse>.SuccessResponse(loginResponse, "Google account successfully linked to your SmartBank profile!"));
        }

        public async Task<(int StatusCode, ApiResponse<RegisterResponse> Response)> CompleteGoogleRegistrationAsync(CompleteGoogleRegistrationRequest request, string? ipAddress)
        {
            var normalizedUsername = request.Username.Trim().ToLowerInvariant();
            var normalizedEmail = request.Email.Trim().ToLowerInvariant();

            // Check if user already registered and is Pending or Rejected
            var emailUser = await _context.Users
                .Include(u => u.Accounts)
                .Include(u => u.ExternalLogins)
                .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

            if (emailUser != null)
            {
                if (emailUser.Status != null && emailUser.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
                {
                    return (201, ApiResponse<RegisterResponse>.SuccessResponse(new RegisterResponse
                    {
                        UserId = emailUser.Id,
                        Username = emailUser.Username,
                        FullName = emailUser.FullName,
                        Role = emailUser.Role
                    }, $"Registration submitted successfully! Your account with NID: {emailUser.NidNumber ?? request.NidNumber.Trim()} is pending administrator verification. Once an Admin approves your account, you will be able to sign in."));
                }
                else if (emailUser.Status != null && emailUser.Status.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
                {
                    _context.ExternalLogins.RemoveRange(emailUser.ExternalLogins);
                    _context.Accounts.RemoveRange(emailUser.Accounts);
                    _context.Users.Remove(emailUser);
                    await _context.SaveChangesAsync();
                }
                else
                {
                    return (409, ApiResponse<RegisterResponse>.FailureResponse("An account with this email address already exists. Please sign in or link your account instead."));
                }
            }

            var usernameUser = await _context.Users
                .Include(u => u.Accounts)
                .Include(u => u.ExternalLogins)
                .FirstOrDefaultAsync(u => u.Username.ToLower() == normalizedUsername);

            if (usernameUser != null)
            {
                if (usernameUser.Status != null && usernameUser.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
                {
                    return (201, ApiResponse<RegisterResponse>.SuccessResponse(new RegisterResponse
                    {
                        UserId = usernameUser.Id,
                        Username = usernameUser.Username,
                        FullName = usernameUser.FullName,
                        Role = usernameUser.Role
                    }, $"Registration submitted successfully! Your account with NID: {usernameUser.NidNumber ?? request.NidNumber.Trim()} is pending administrator verification. Once an Admin approves your account, you will be able to sign in."));
                }
                else if (usernameUser.Status != null && usernameUser.Status.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
                {
                    _context.ExternalLogins.RemoveRange(usernameUser.ExternalLogins);
                    _context.Accounts.RemoveRange(usernameUser.Accounts);
                    _context.Users.Remove(usernameUser);
                    await _context.SaveChangesAsync();
                }
                else
                {
                    return (409, ApiResponse<RegisterResponse>.FailureResponse("This username is already taken. Please choose another username."));
                }
            }

            var accountNumber = await GenerateUnique12DigitAccountNumberAsync();
            var rawPassword = !string.IsNullOrWhiteSpace(request.Password) ? request.Password : (Guid.NewGuid().ToString("N") + "!Aa1");
            var passwordHash = PasswordHasher.HashPassword(rawPassword);

            var now = DateTime.UtcNow;
            var user = new User
            {
                FullName = request.FullName.Trim(),
                Email = normalizedEmail,
                PhoneNumber = request.PhoneNumber.Trim(),
                NidNumber = request.NidNumber.Trim(),
                Username = normalizedUsername,
                PasswordHash = passwordHash,
                Role = "Customer", // NEVER ALLOW ADMIN
                Status = "Pending",
                IsEmailVerified = true, // Verified by Google OAuth
                EmailVerifiedAt = now,
                FailedLoginCount = 0,
                LockedUntil = null,
                CreatedAt = now,
                UpdatedAt = now
            };

            var initialBalance = 2500.00m;
            var account = new Account
            {
                AccountNumber = accountNumber,
                Balance = initialBalance,
                IsActive = false,
                CreatedAt = now,
                UpdatedAt = now
            };

            user.Accounts.Add(account);

            var externalLogin = new ExternalLogin
            {
                Provider = "Google",
                ProviderUserId = request.GoogleSubjectId.Trim(),
                Email = normalizedEmail,
                CreatedAt = now,
                LastLoginAt = now
            };

            user.ExternalLogins.Add(externalLogin);

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

                return (201, ApiResponse<RegisterResponse>.SuccessResponse(responseData, "Google registration completed successfully!"));
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return (500, ApiResponse<RegisterResponse>.FailureResponse("Failed to complete Google registration: " + ex.Message));
            }
        }

        public async Task<(int StatusCode, ApiResponse<bool> Response)> SendEmailVerificationOtpAsync(int userId)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                return (404, ApiResponse<bool>.FailureResponse("User not found"));
            }

            if (user.IsEmailVerified)
            {
                return (200, ApiResponse<bool>.SuccessResponse(true, "Your email is already verified."));
            }

            var (challenge, plainOtp) = await _otpService.CreateChallengeAsync(userId, "EmailVerification", userId.ToString(), expiryMinutes: 5);
            await _emailService.SendEmailVerificationOtpAsync(user.Email, plainOtp);

            return (200, ApiResponse<bool>.SuccessResponse(true, $"A 6-digit verification code has been dispatched to {user.Email}."));
        }

        public async Task<(int StatusCode, ApiResponse<bool> Response)> VerifyEmailOtpAsync(int userId, string otp)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                return (404, ApiResponse<bool>.FailureResponse("User not found"));
            }

            if (user.IsEmailVerified)
            {
                return (200, ApiResponse<bool>.SuccessResponse(true, "Your email is already verified."));
            }

            var (isValid, errorMsg) = await _otpService.ValidateChallengeAsync(userId, "EmailVerification", userId.ToString(), otp);
            if (!isValid)
            {
                return (400, ApiResponse<bool>.FailureResponse(errorMsg ?? "Invalid verification code."));
            }

            user.IsEmailVerified = true;
            user.EmailVerifiedAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return (200, ApiResponse<bool>.SuccessResponse(true, "Email verified successfully! You can now initiate fund transfers."));
        }
    }
}
