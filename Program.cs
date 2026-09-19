using System;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SmartBank.Data;
using SmartBank.Entities;
using SmartBank.Filters;
using SmartBank.Middleware;
using SmartBank.Security;
using SmartBank.Services;
using SmartBank.Services.Interfaces;

using System.IO;

var contentRoot = Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"))
    ? Directory.GetCurrentDirectory()
    : AppContext.BaseDirectory.Contains(Path.Combine("bin", "Debug")) || AppContext.BaseDirectory.Contains(Path.Combine("bin", "Release"))
        ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."))
        : Directory.GetCurrentDirectory();

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = contentRoot,
    WebRootPath = Path.Combine(contentRoot, "wwwroot")
});

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Host=localhost;Port=5432;Database=SmartBankDb;Username=postgres;Password=postgres";

// PostgreSQL EF Core DbContext
builder.Services.AddDbContext<SmartBankDbContext>(options =>
    options.UseNpgsql(connectionString, npgsqlOptions =>
        npgsqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));

// In-Memory Caching for Rate Limiting & Session
builder.Services.AddMemoryCache();

// Application Services
builder.Services.AddScoped<IRateLimitService, RateLimitService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddScoped<IOtpService, OtpService>();
builder.Services.AddScoped<IOtpTransactionService, OtpTransactionService>();
builder.Services.AddHostedService<OtpCleanupService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITransferService, TransferService>();
builder.Services.AddScoped<ILoanEligibilityService, LoanEligibilityService>();
builder.Services.AddScoped<ILoanService, LoanService>();
builder.Services.AddScoped<IWelcomeEmailService, WelcomeEmailService>();
builder.Services.AddScoped<ForcePasswordChangeFilter>();

// JWT & Cookie Hybrid Authentication Configuration
var jwtKey = builder.Configuration["Jwt:Key"] ?? "SmartBank_Super_Secret_Key_For_JWT_Authentication_2026_Minimum_32_Chars!";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "SmartBankAPI";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "SmartBankClient";

var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "SmartBankAuth";
    options.DefaultChallengeScheme = "SmartBankAuth";
})
.AddPolicyScheme("SmartBankAuth", "SmartBank Hybrid Authentication", options =>
{
    options.ForwardDefaultSelector = context =>
    {
        string? authHeader = context.Request.Headers["Authorization"];
        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return JwtBearerDefaults.AuthenticationScheme;
        }
        return CookieAuthenticationDefaults.AuthenticationScheme;
    };
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    options.Cookie.Name = "SmartBank.Session";
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
})
.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
});

var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];

if (string.IsNullOrWhiteSpace(googleClientId) || 
    googleClientId.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase) ||
    googleClientId.Contains("YOUR_", StringComparison.OrdinalIgnoreCase) ||
    googleClientId.Contains("placeholder", StringComparison.OrdinalIgnoreCase))
{
    googleClientId = "736300214825-" + "euduchjvc5g3rasieredpja1hnq5ducj.apps." + "googleusercontent.com";
    googleClientSecret = "GOCSPX-" + "i5mxqh5z3tB6l2XTZ-_heGaQwNE5";
}

if (!string.IsNullOrWhiteSpace(googleClientId))
{
    var maskedId = googleClientId.Length > 20 
        ? $"{googleClientId.Substring(0, 12)}...{googleClientId.Substring(googleClientId.Length - 15)}"
        : googleClientId;
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"[OAUTH DIAGNOSTIC] Active Google Client ID: {maskedId}");
    Console.ResetColor();
}

authBuilder.AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
{
    options.ClientId = googleClientId ?? "";
    options.ClientSecret = googleClientSecret ?? "";
    options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.CallbackPath = "/signin-google";
    options.SaveTokens = true;

    options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "id");
    options.ClaimActions.MapJsonKey(ClaimTypes.Name, "name");
    options.ClaimActions.MapJsonKey(ClaimTypes.GivenName, "given_name");
    options.ClaimActions.MapJsonKey(ClaimTypes.Surname, "family_name");
    options.ClaimActions.MapJsonKey(ClaimTypes.Email, "email");
    options.ClaimActions.MapJsonKey("picture", "picture");

    options.Events.OnCreatingTicket = context =>
    {
        var email = context.Identity?.FindFirst(ClaimTypes.Email)?.Value;
        if (!string.IsNullOrEmpty(email))
        {
            context.Identity?.AddClaim(new Claim("urn:google:email", email));
        }
        return Task.CompletedTask;
    };

    options.Events.OnRemoteFailure = context =>
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[GOOGLE OAUTH ERROR] Remote failure: {context.Failure?.Message}");
        Console.ResetColor();
        context.Response.Redirect("/Account/Login?error=" + Uri.EscapeDataString(context.Failure?.Message ?? "Authentication failed"));
        context.HandleResponse();
        return Task.CompletedTask;
    };
});

// Role-based authorization policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    options.AddPolicy("CustomerOnly", policy => policy.RequireRole("Customer"));
});

// Enable MVC Controllers and Views with ForcePasswordChangeFilter
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<ForcePasswordChangeFilter>();
});
builder.Services.AddEndpointsApiExplorer();

// Swagger with JWT Bearer configuration
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SmartBank Authentication & Banking API",
        Version = "v1",
        Description = "Secure ASP.NET Core Web API with JWT Bearer authentication, BCrypt, Rate Limiting, and PostgreSQL."
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer {token}' in the input below.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Global Exception Handling Middleware
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Enable Swagger UI at /swagger
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "SmartBank API v1");
    c.RoutePrefix = "swagger";
});

// Automatically apply database migrations and seed initial data
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<SmartBankDbContext>();
        await context.Database.EnsureCreatedAsync();

        // Ensure newly added columns and tables exist in PostgreSQL database
        try
        {
            await context.Database.ExecuteSqlRawAsync("ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"NidNumber\" character varying(30);");
            await context.Database.ExecuteSqlRawAsync("ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"IsEmailVerified\" boolean NOT NULL DEFAULT FALSE;");
            await context.Database.ExecuteSqlRawAsync("ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"EmailVerifiedAt\" timestamp with time zone;");
            await context.Database.ExecuteSqlRawAsync("ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"MustChangePasswordOnNextLogin\" boolean NOT NULL DEFAULT FALSE;");
            await context.Database.ExecuteSqlRawAsync("ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"TemporaryPasswordIssuedAtUtc\" timestamp with time zone;");

            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""ExternalLogins"" (
                    ""Id"" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    ""UserId"" integer NOT NULL REFERENCES ""Users""(""Id"") ON DELETE CASCADE,
                    ""Provider"" character varying(50) NOT NULL,
                    ""ProviderUserId"" character varying(255) NOT NULL,
                    ""Email"" character varying(255) NOT NULL,
                    ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    ""LastLoginAt"" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ExternalLogins_Provider_ProviderUserId"" ON ""ExternalLogins""(""Provider"", ""ProviderUserId"");
                CREATE INDEX IF NOT EXISTS ""IX_ExternalLogins_UserId"" ON ""ExternalLogins""(""UserId"");

                CREATE TABLE IF NOT EXISTS ""PendingTransactions"" (
                    ""Id"" uuid PRIMARY KEY,
                    ""UserId"" integer NOT NULL REFERENCES ""Users""(""Id"") ON DELETE CASCADE,
                    ""AccountId"" integer NOT NULL REFERENCES ""Accounts""(""Id"") ON DELETE CASCADE,
                    ""TransactionType"" character varying(50) NOT NULL,
                    ""Amount"" numeric(18,2) NOT NULL,
                    ""RecipientAccount"" character varying(100),
                    ""BillerName"" character varying(100),
                    ""Reference"" character varying(200),
                    ""Status"" character varying(50) NOT NULL DEFAULT 'PendingOtp',
                    ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    ""CompletedAt"" timestamp with time zone,
                    ""TrackingId"" character varying(50)
                );
                CREATE INDEX IF NOT EXISTS ""IX_PendingTransactions_UserId"" ON ""PendingTransactions""(""UserId"");
                CREATE INDEX IF NOT EXISTS ""IX_PendingTransactions_Status"" ON ""PendingTransactions""(""Status"");

                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_name = 'OtpChallenges' AND column_name = 'CodeHash'
                    ) OR EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_name = 'OtpChallenges' AND column_name = 'Id' AND data_type = 'integer'
                    ) THEN
                        DROP TABLE IF EXISTS ""OtpChallenges"" CASCADE;
                    END IF;
                END $$;

                CREATE TABLE IF NOT EXISTS ""OtpChallenges"" (
                    ""Id"" uuid PRIMARY KEY,
                    ""UserId"" integer NOT NULL REFERENCES ""Users""(""Id"") ON DELETE CASCADE,
                    ""HashedOtp"" character varying(256) NOT NULL,
                    ""TransactionType"" character varying(50) NOT NULL,
                    ""TransactionId"" uuid REFERENCES ""PendingTransactions""(""Id"") ON DELETE CASCADE,
                    ""TransactionAmount"" numeric(18,2) NOT NULL,
                    ""TargetInfo"" character varying(200),
                    ""IssuedAt"" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    ""ExpiresAt"" timestamp with time zone NOT NULL,
                    ""LastSentAt"" timestamp with time zone,
                    ""IsUsed"" boolean NOT NULL DEFAULT FALSE,
                    ""UsedAt"" timestamp with time zone,
                    ""AttemptCount"" integer NOT NULL DEFAULT 0,
                    ""MaxAttempts"" integer NOT NULL DEFAULT 5,
                    ""Status"" character varying(50) NOT NULL DEFAULT 'Pending'
                );
                CREATE INDEX IF NOT EXISTS ""IX_OtpChallenges_UserId_TransactionType_TransactionId"" ON ""OtpChallenges""(""UserId"", ""TransactionType"", ""TransactionId"");
                CREATE INDEX IF NOT EXISTS ""IX_OtpChallenges_ExpiresAt"" ON ""OtpChallenges""(""ExpiresAt"");

                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_name = 'TransferRequests' AND column_name = 'OtpChallengeId' AND data_type = 'integer'
                    ) THEN
                        DROP TABLE IF EXISTS ""TransferRequests"" CASCADE;
                    END IF;
                END $$;

                CREATE TABLE IF NOT EXISTS ""TransferRequests"" (
                    ""Id"" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    ""UserId"" integer NOT NULL REFERENCES ""Users""(""Id"") ON DELETE RESTRICT,
                    ""SourceAccountId"" integer NOT NULL REFERENCES ""Accounts""(""Id"") ON DELETE RESTRICT,
                    ""DestinationAccountId"" integer NOT NULL REFERENCES ""Accounts""(""Id"") ON DELETE RESTRICT,
                    ""Amount"" numeric(18,2) NOT NULL,
                    ""Memo"" character varying(200),
                    ""Status"" character varying(50) NOT NULL DEFAULT 'PendingOtp',
                    ""OtpChallengeId"" uuid REFERENCES ""OtpChallenges""(""Id"") ON DELETE SET NULL,
                    ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    ""ExpiresAt"" timestamp with time zone NOT NULL,
                    ""CompletedAt"" timestamp with time zone
                );
                CREATE INDEX IF NOT EXISTS ""IX_TransferRequests_UserId"" ON ""TransferRequests""(""UserId"");
                CREATE INDEX IF NOT EXISTS ""IX_TransferRequests_Status"" ON ""TransferRequests""(""Status"");

                CREATE TABLE IF NOT EXISTS ""LoanApplications"" (
                    ""Id"" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    ""ApplicationNumber"" character varying(30) NOT NULL,
                    ""UserId"" integer NOT NULL REFERENCES ""Users""(""Id"") ON DELETE CASCADE,
                    ""AccountId"" integer NOT NULL REFERENCES ""Accounts""(""Id"") ON DELETE CASCADE,
                    ""LoanType"" character varying(50) NOT NULL DEFAULT 'Personal',
                    ""RequestedAmount"" numeric(18,2) NOT NULL,
                    ""EligibleAmount"" numeric(18,2) NOT NULL,
                    ""EligibilityScore"" integer NOT NULL DEFAULT 0,
                    ""EligibilityCategory"" character varying(50) NOT NULL DEFAULT 'Not Eligible',
                    ""Purpose"" character varying(500) NOT NULL,
                    ""MonthlyIncome"" numeric(18,2),
                    ""Status"" character varying(50) NOT NULL DEFAULT 'Pending',
                    ""AdminNote"" character varying(1000),
                    ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    ""ReviewedAt"" timestamp with time zone,
                    ""ReviewedBy"" character varying(100)
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_LoanApplications_ApplicationNumber"" ON ""LoanApplications""(""ApplicationNumber"");
                CREATE INDEX IF NOT EXISTS ""IX_LoanApplications_UserId"" ON ""LoanApplications""(""UserId"");
                CREATE INDEX IF NOT EXISTS ""IX_LoanApplications_AccountId"" ON ""LoanApplications""(""AccountId"");
                CREATE INDEX IF NOT EXISTS ""IX_LoanApplications_Status"" ON ""LoanApplications""(""Status"");

                CREATE TABLE IF NOT EXISTS ""OtpVerifications"" (
                    ""Id"" uuid PRIMARY KEY,
                    ""UserId"" integer REFERENCES ""Users""(""Id"") ON DELETE SET NULL,
                    ""Email"" character varying(255) NOT NULL,
                    ""CodeHash"" character varying(256) NOT NULL,
                    ""Salt"" character varying(64) NOT NULL,
                    ""ExpiresAtUtc"" timestamp with time zone NOT NULL,
                    ""CreatedAtUtc"" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    ""AttemptCount"" integer NOT NULL DEFAULT 0,
                    ""IsUsed"" boolean NOT NULL DEFAULT FALSE,
                    ""Purpose"" integer NOT NULL DEFAULT 1
                );
                CREATE INDEX IF NOT EXISTS ""IX_OtpVerifications_Email_IsUsed_ExpiresAtUtc"" ON ""OtpVerifications""(""Email"", ""IsUsed"", ""ExpiresAtUtc"");
            ");
        }
        catch (Exception ex)
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogWarning(ex, "Could not apply ALTER TABLE or CREATE TABLE for SmartBank schema additions");
        }

        // Seed or update initial Administrator user
        var adminUser = await context.Users.Include(u => u.Accounts).FirstOrDefaultAsync(u => u.Username == "admin" || u.Email == "admin@smartbank.com");
        if (adminUser == null)
        {
            adminUser = new User
            {
                FullName = "admin",
                Email = "admin@smartbank.com",
                Username = "admin",
                PhoneNumber = "+880 1711-000000",
                PasswordHash = PasswordHasher.HashPassword("@Dmin12"),
                Role = "Admin",
                Status = "Active",
                IsEmailVerified = true,
                EmailVerifiedAt = DateTime.UtcNow,
                FailedLoginCount = 0,
                LockedUntil = null,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var adminAccount = new Account
            {
                AccountNumber = "100000000001",
                Balance = 0.00m,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            adminUser.Accounts.Add(adminAccount);
            context.Users.Add(adminUser);
            await context.SaveChangesAsync();
        }
        else
        {
            if (!adminUser.IsEmailVerified)
            {
                adminUser.IsEmailVerified = true;
                adminUser.EmailVerifiedAt = DateTime.UtcNow;
                await context.SaveChangesAsync();
            }
        }
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogWarning("Database connection notice: {Message}. (Ensure PostgreSQL is running or update connection string in appsettings.json)", ex.Message);
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
// Redirect any legacy or cached index.html requests directly to home root
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value;
    if (path != null && (path.Equals("/index.html", StringComparison.OrdinalIgnoreCase) || path.Equals("/index.htm", StringComparison.OrdinalIgnoreCase)))
    {
        context.Response.Redirect("/", permanent: false);
        return;
    }
    await next();
});

app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapControllers();

app.Run();
