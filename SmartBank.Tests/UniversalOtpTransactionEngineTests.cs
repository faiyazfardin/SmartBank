using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using SmartBank.Data;
using SmartBank.DTOs.Transactions;
using SmartBank.Entities;
using SmartBank.Services;
using SmartBank.Services.Interfaces;
using Xunit;

namespace SmartBank.Tests
{
    public class UniversalOtpTransactionEngineTests
    {
        private SmartBankDbContext GetInMemoryDbContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<SmartBankDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            return new SmartBankDbContext(options);
        }

        private (User User, Account Account) SeedUserAndAccount(SmartBankDbContext context, decimal initialBalance = 1000m)
        {
            var user = new User
            {
                Id = 1,
                Username = "testuser",
                Email = "testuser@smartbank.com",
                FullName = "Test User",
                PasswordHash = "hashedpass",
                Role = "Customer",
                Status = "Active",
                IsEmailVerified = true
            };

            var account = new Account
            {
                Id = 1,
                UserId = 1,
                AccountNumber = "ACC100000001",
                Balance = initialBalance,
                IsActive = true
            };

            context.Users.Add(user);
            context.Accounts.Add(account);
            context.SaveChanges();

            return (user, account);
        }

        [Fact]
        public async Task InitiateTransaction_GeneratesOtp_ValidFor120Seconds()
        {
            // Arrange
            using var context = GetInMemoryDbContext("Db_Initiate_120s");
            SeedUserAndAccount(context);

            var emailServiceMock = new Mock<IEmailService>();
            var loggerMock = new Mock<ILogger<OtpTransactionService>>();
            var service = new OtpTransactionService(context, emailServiceMock.Object, loggerMock.Object);

            // Act
            var (statusCode, response) = await service.InitiateTransactionAsync(1, new TransactionRequestDto
            {
                TransactionType = "Deposit",
                Amount = 500m
            });

            // Assert
            Assert.Equal(200, statusCode);
            Assert.True(response.Success);
            Assert.NotNull(response.Data);

            var challenge = await context.OtpChallenges.FirstOrDefaultAsync(c => c.Id == response.Data.ChallengeId);
            Assert.NotNull(challenge);
            Assert.False(challenge.IsUsed);
            Assert.Equal("Pending", challenge.Status);
            Assert.Equal(120, (challenge.ExpiresAt - challenge.IssuedAt).TotalSeconds, 1);
        }

        [Fact]
        public async Task VerifyOtp_ExpiredAfter120s_RejectsVerification()
        {
            // Arrange
            using var context = GetInMemoryDbContext("Db_Verify_Expired");
            SeedUserAndAccount(context);

            var emailServiceMock = new Mock<IEmailService>();
            var loggerMock = new Mock<ILogger<OtpTransactionService>>();
            var service = new OtpTransactionService(context, emailServiceMock.Object, loggerMock.Object);

            var (statusCode, initiateRes) = await service.InitiateTransactionAsync(1, new TransactionRequestDto
            {
                TransactionType = "Withdraw",
                Amount = 200m
            });

            var challenge = await context.OtpChallenges.FirstOrDefaultAsync(c => c.Id == initiateRes.Data!.ChallengeId);
            Assert.NotNull(challenge);

            // Manually expire challenge
            challenge.ExpiresAt = DateTime.UtcNow.AddSeconds(-10);
            await context.SaveChangesAsync();

            // Act
            var (verifyCode, verifyRes) = await service.VerifyOtpAndCommitAsync(challenge.Id, "123456");

            // Assert
            Assert.Equal(400, verifyCode);
            Assert.False(verifyRes.Success);
            Assert.Contains("OTP expired", verifyRes.Message);
        }

        [Fact]
        public async Task VerifyOtp_CrossTransactionIsolation_RejectsMismatch()
        {
            // Arrange
            using var context = GetInMemoryDbContext("Db_CrossTransaction_Isolation");
            SeedUserAndAccount(context);

            var emailServiceMock = new Mock<IEmailService>();
            var loggerMock = new Mock<ILogger<OtpTransactionService>>();
            var service = new OtpTransactionService(context, emailServiceMock.Object, loggerMock.Object);

            // Initiate Deposit A
            var (_, initResA) = await service.InitiateTransactionAsync(1, new TransactionRequestDto
            {
                TransactionType = "Deposit",
                Amount = 100m
            });

            var challengeA = await context.OtpChallenges.FirstOrDefaultAsync(c => c.Id == initResA.Data!.ChallengeId);
            Assert.NotNull(challengeA);

            // Tamper challengeA's TransactionId to point to an unrelated non-existent transaction Guid
            challengeA.TransactionId = Guid.NewGuid();
            await context.SaveChangesAsync();

            // Act
            var (verifyCode, verifyRes) = await service.VerifyOtpAndCommitAsync(challengeA.Id, "123456");

            // Assert
            Assert.Equal(404, verifyCode);
            Assert.False(verifyRes.Success);
            Assert.Contains("Associated pending transaction not found", verifyRes.Message);
        }

        [Fact]
        public async Task VerifyOtp_FiveFailedAttempts_LocksChallenge()
        {
            // Arrange
            using var context = GetInMemoryDbContext("Db_Attempt_Lock");
            SeedUserAndAccount(context);

            var emailServiceMock = new Mock<IEmailService>();
            var loggerMock = new Mock<ILogger<OtpTransactionService>>();
            var service = new OtpTransactionService(context, emailServiceMock.Object, loggerMock.Object);

            var (_, initiateRes) = await service.InitiateTransactionAsync(1, new TransactionRequestDto
            {
                TransactionType = "Deposit",
                Amount = 300m
            });

            var challengeId = initiateRes.Data!.ChallengeId;

            // Fail 5 times with wrong OTP "000000"
            for (int i = 0; i < 5; i++)
            {
                await service.VerifyOtpAndCommitAsync(challengeId, "000000");
            }

            // Act - 6th attempt
            var (verifyCode, verifyRes) = await service.VerifyOtpAndCommitAsync(challengeId, "000000");

            // Assert
            Assert.Equal(400, verifyCode);
            Assert.False(verifyRes.Success);
            Assert.Contains("locked", verifyRes.Message, StringComparison.OrdinalIgnoreCase);

            var challenge = await context.OtpChallenges.FirstOrDefaultAsync(c => c.Id == challengeId);
            Assert.Equal("Locked", challenge!.Status);
        }

        [Fact]
        public async Task ResendOtp_Enforces60sCooldown()
        {
            // Arrange
            using var context = GetInMemoryDbContext("Db_Resend_Cooldown");
            SeedUserAndAccount(context);

            var emailServiceMock = new Mock<IEmailService>();
            var loggerMock = new Mock<ILogger<OtpTransactionService>>();
            var service = new OtpTransactionService(context, emailServiceMock.Object, loggerMock.Object);

            var (_, initiateRes) = await service.InitiateTransactionAsync(1, new TransactionRequestDto
            {
                TransactionType = "Deposit",
                Amount = 500m
            });

            var challengeId = initiateRes.Data!.ChallengeId;

            // Act - Immediate Resend attempt within 60s
            var (resendCode, resendRes) = await service.ResendOtpAsync(challengeId);

            // Assert
            Assert.Equal(429, resendCode);
            Assert.False(resendRes.Success);
            Assert.Contains("Please wait", resendRes.Message);
        }
    }
}
