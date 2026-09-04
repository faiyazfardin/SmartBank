using System;
using System.Text;
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

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Host=localhost;Port=5432;Database=SmartBankDb;Username=postgres;Password=postgres";

// PostgreSQL EF Core DbContext
builder.Services.AddDbContext<SmartBankDbContext>(options =>
    options.UseNpgsql(connectionString));

// In-Memory Caching for Rate Limiting
builder.Services.AddMemoryCache();

// Application Services
builder.Services.AddScoped<IRateLimitService, RateLimitService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// JWT Authentication Configuration
var jwtKey = builder.Configuration["Jwt:Key"] ?? "SmartBank_Super_Secret_Key_For_JWT_Authentication_2026_Minimum_32_Chars!";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "SmartBankAPI";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "SmartBankClient";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
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

builder.Services.AddControllers();
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

// Enable Swagger in Development and Production for testing
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "SmartBank API v1");
    c.RoutePrefix = string.Empty; // Serve Swagger at app root URL
});

// Automatically apply database migrations and seed initial data
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<SmartBankDbContext>();
        await context.Database.EnsureCreatedAsync();

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
                Balance = 50000.00m,
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
            // Update existing admin credentials to @Dmin12 and ensure active
            adminUser.FullName = "admin";
            adminUser.PasswordHash = PasswordHasher.HashPassword("@Dmin12");
            adminUser.Role = "Admin";
            adminUser.Status = "Active";
            adminUser.FailedLoginCount = 0;
            adminUser.LockedUntil = null;
            adminUser.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogWarning("Database connection notice: {Message}. (Ensure PostgreSQL is running or update connection string in appsettings.json)", ex.Message);
    }
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
