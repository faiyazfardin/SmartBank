using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartBank.Data;
using SmartBank.Entities;

namespace SmartBank.Services
{
    public class RiskDecayBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<RiskDecayBackgroundService> _logger;

        public RiskDecayBackgroundService(IServiceProvider serviceProvider, ILogger<RiskDecayBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[RISK DECAY ENGINE] Hourly Risk Score Decay background service initialized.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PerformHourlyDecayAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[RISK DECAY ENGINE] Error executing hourly risk score decay cycle.");
                }

                // Run every 1 hour (3600 seconds)
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }

        private async Task PerformHourlyDecayAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<SmartBankDbContext>();

            var usersToDecay = await context.Users
                .Where(u => u.RiskScore > 0)
                .ToListAsync();

            if (!usersToDecay.Any()) return;

            int processedCount = 0;
            foreach (var user in usersToDecay)
            {
                int oldScore = user.RiskScore;
                int newScore = Math.Max(0, oldScore - 2); // Decay by 2 points per hour

                user.RiskScore = newScore;
                user.UpdatedAt = DateTime.UtcNow;

                var decayEvent = new RiskEvent
                {
                    UserId = user.Id,
                    EventType = "Decay",
                    Points = -2,
                    ScoreAfter = newScore,
                    Timestamp = DateTime.UtcNow,
                    Metadata = "{\"action\": \"Hourly Decay -2 pts\"}",
                    IsDecay = true
                };

                context.RiskEvents.Add(decayEvent);

                // Note: Decay MUST NOT auto-unfreeze accounts!
                // Even if RiskScore becomes <= 80, user.Status remains "Suspended" until Admin unfreezes.

                processedCount++;
            }

            await context.SaveChangesAsync();
            _logger.LogInformation("[RISK DECAY ENGINE] Completed hourly decay cycle for {Count} users with RiskScore > 0.", processedCount);
        }
    }
}
