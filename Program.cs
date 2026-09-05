using System;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
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
    options.UseNpgsql(connectionString));

// In-Memory Caching for Rate Limiting & Session
builder.Services.AddMemoryCache();

// Application Services
builder.Services.AddScoped<IRateLimitService, RateLimitService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ILoanEligibilityService, LoanEligibilityService>();
builder.Services.AddScoped<ILoanService, LoanService>();

// JWT & Cookie Hybrid Authentication Configuration
var jwtKey = builder.Configuration["Jwt:Key"] ?? "SmartBank_Super_Secret_Key_For_JWT_Authentication_2026_Minimum_32_Chars!";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "SmartBankAPI";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "SmartBankClient";

builder.Services.AddAuthentication(options =>
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

// Role-based authorization policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    options.AddPolicy("CustomerOnly", policy => policy.RequireRole("Customer"));
});

// Enable MVC Controllers and Views
builder.Services.AddControllersWithViews();
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
            await context.Database.ExecuteSqlRawAsync(@"
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
            ");
        }
        catch (Exception ex)
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogWarning(ex, "Could not apply ALTER TABLE or CREATE TABLE for LoanApplications");
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
