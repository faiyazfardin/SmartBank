using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartBank.Data;

namespace SmartBank.Services
{
    public class OtpCleanupService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<OtpCleanupService> _logger;
        private readonly TimeSpan _period = TimeSpan.FromSeconds(30);

        public OtpCleanupService(IServiceProvider serviceProvider, ILogger<OtpCleanupService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("OtpCleanupService background worker started.");

            using var timer = new PeriodicTimer(_period);
            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<SmartBankDbContext>();

                    var now = DateTime.UtcNow;

                    // 1. Mark expired challenges
                    var expiredChallenges = await dbContext.OtpChallenges
                        .Where(c => !c.IsUsed && c.Status == "Pending" && c.ExpiresAt < now)
                        .ToListAsync(stoppingToken);

                    if (expiredChallenges.Any())
                    {
                        foreach (var challenge in expiredChallenges)
                        {
                            challenge.Status = "Expired";
                        }

                        var txIds = expiredChallenges.Select(c => c.TransactionId).ToList();
                        var expiredPendingTxs = await dbContext.PendingTransactions
                            .Where(p => txIds.Contains(p.Id) && p.Status == "PendingOtp")
                            .ToListAsync(stoppingToken);

                        foreach (var tx in expiredPendingTxs)
                        {
                            tx.Status = "Expired";
                        }

                        await dbContext.SaveChangesAsync(stoppingToken);
                        _logger.LogInformation("OtpCleanupService: Marked {Count} expired challenges.", expiredChallenges.Count);
                    }

                    // 2. Purge challenges older than 24 hours
                    var cutoff = now.AddHours(-24);
                    var oldChallenges = await dbContext.OtpChallenges
                        .Where(c => c.IssuedAt < cutoff)
                        .ToListAsync(stoppingToken);

                    if (oldChallenges.Any())
                    {
                        dbContext.OtpChallenges.RemoveRange(oldChallenges);
                        await dbContext.SaveChangesAsync(stoppingToken);
                        _logger.LogInformation("OtpCleanupService: Purged {Count} challenges older than 24h.", oldChallenges.Count);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during OtpCleanupService background execution.");
                }
            }
        }
    }
}
