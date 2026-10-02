using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartBank.Data;
using SmartBank.Entities;
using SmartBank.Security;
using SmartBank.Services.Interfaces;

namespace SmartBank.Services
{
    public class RiskService : IRiskService
    {
        private readonly SmartBankDbContext _context;
        private readonly ILogger<RiskService> _logger;

        public RiskService(SmartBankDbContext context, ILogger<RiskService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<(int NewScore, bool IsFrozen)> RecordRiskEventAsync(
            int userId,
            string eventType,
            int points,
            string? metadataJson = null,
            bool isDecay = false)
        {
            var user = await _context.Users
                .Include(u => u.Accounts)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                return (0, false);
            }

            int currentScore = user.RiskScore;
            int newScore = Math.Clamp(currentScore + points, 0, 100);
            user.RiskScore = newScore;
            user.UpdatedAt = DateTime.UtcNow;

            var riskEvent = new RiskEvent
            {
                UserId = userId,
                EventType = eventType,
                Points = points,
                ScoreAfter = newScore,
                Timestamp = DateTime.UtcNow,
                Metadata = metadataJson,
                IsDecay = isDecay
            };
            _context.RiskEvents.Add(riskEvent);

            bool isFrozen = string.Equals(user.Status, "Suspended", StringComparison.OrdinalIgnoreCase);

            // CORE RULE: If RiskScore > 80 -> FREEZE the account (unless decay)
            if (newScore > 80 && !isFrozen)
            {
                user.Status = "Suspended";
                user.LockedUntil = DateTime.UtcNow.AddYears(100);

                foreach (var acc in user.Accounts)
                {
                    acc.IsActive = false;
                    acc.UpdatedAt = DateTime.UtcNow;
                }

                isFrozen = true;
                _logger.LogWarning("[RISK ENGINE - ACCOUNT FROZEN] User {UserId} (@{Username}) RiskScore exceeded 80 (Score: {NewScore}). Account & all bank accounts suspended.", user.Id, user.Username, newScore);
            }

            await _context.SaveChangesAsync();
            return (newScore, isFrozen);
        }

        public async Task AdminUnfreezeUserAsync(int userId, string adminUsername)
        {
            var user = await _context.Users
                .Include(u => u.Accounts)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null) return;

            // RULE: On admin unfreeze: set RiskScore = 70.
            user.RiskScore = 70;
            user.Status = "Active";
            user.LockedUntil = null;
            user.FailedLoginCount = 0;
            user.FailedVaultAttempts = 0;
            user.VaultLockedUntil = null;
            user.UpdatedAt = DateTime.UtcNow;

            foreach (var acc in user.Accounts)
            {
                acc.IsActive = true;
                acc.UpdatedAt = DateTime.UtcNow;
            }

            var metadata = JsonSerializer.Serialize(new
            {
                admin = adminUsername,
                action = "Admin Unfreeze Reset Score to 70",
                timestamp = DateTime.UtcNow
            });

            var riskEvent = new RiskEvent
            {
                UserId = userId,
                EventType = "AdminUnfreeze",
                Points = 0,
                ScoreAfter = 70,
                Timestamp = DateTime.UtcNow,
                Metadata = metadata,
                IsDecay = false
            };
            _context.RiskEvents.Add(riskEvent);

            await _context.SaveChangesAsync();
            _logger.LogInformation("[RISK ENGINE] User {UserId} unfrozen by Admin {Admin}. RiskScore set to 70.", user.Id, adminUsername);
        }

        public async Task<int> AdminManualAdjustScoreAsync(int userId, int pointsAdjustment, string reason, string adminUsername)
        {
            var user = await _context.Users
                .Include(u => u.Accounts)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null) return 0;

            int newScore = Math.Clamp(user.RiskScore + pointsAdjustment, 0, 100);
            user.RiskScore = newScore;
            user.UpdatedAt = DateTime.UtcNow;

            var metadata = JsonSerializer.Serialize(new
            {
                admin = adminUsername,
                reason = reason,
                adjustment = pointsAdjustment,
                timestamp = DateTime.UtcNow
            });

            var riskEvent = new RiskEvent
            {
                UserId = userId,
                EventType = "AdminManualAdjust",
                Points = pointsAdjustment,
                ScoreAfter = newScore,
                Timestamp = DateTime.UtcNow,
                Metadata = metadata,
                IsDecay = false
            };
            _context.RiskEvents.Add(riskEvent);

            if (newScore > 80 && !string.Equals(user.Status, "Suspended", StringComparison.OrdinalIgnoreCase))
            {
                user.Status = "Suspended";
                user.LockedUntil = DateTime.UtcNow.AddYears(100);
                foreach (var acc in user.Accounts)
                {
                    acc.IsActive = false;
                    acc.UpdatedAt = DateTime.UtcNow;
                }
                _logger.LogWarning("[RISK ENGINE - ACCOUNT FROZEN VIA MANUAL ADJUST] User {UserId} RiskScore exceeded 80 ({NewScore}).", user.Id, newScore);
            }

            await _context.SaveChangesAsync();
            return newScore;
        }

        public async Task<StepUpResult> EvaluateTransactionStepUpAsync(
            int userId,
            decimal amount,
            string? providedVaultPassword = null,
            string? recipientInfo = null,
            string? clientIp = null,
            string? deviceHeader = null)
        {
            var user = await _context.Users
                .Include(u => u.Accounts)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                return new StepUpResult { StepUpRequired = false, TransactionAllowed = false, Message = "User not found." };
            }

            // 1. Calculate 30-day average transaction amount
            var past30Days = DateTime.UtcNow.AddDays(-30);
            var accountIds = user.Accounts.Select(a => a.Id).ToList();

            var pastTxs = await _context.Transactions
                .Where(t => accountIds.Contains(t.AccountId) && t.Timestamp >= past30Days)
                .ToListAsync();

            decimal avgAmount = pastTxs.Any() ? pastTxs.Average(t => t.Amount) : 500.00m; // Default benchmark
            if (avgAmount <= 0) avgAmount = 500.00m;

            bool isLarge = amount > 5 * avgAmount;
            bool isVeryLarge = amount > 10 * avgAmount;

            var result = new StepUpResult
            {
                Average30DayAmount = avgAmount,
                Severity = isVeryLarge ? "VeryLarge" : (isLarge ? "Large" : "Normal")
            };

            // Check triggers for new payee / unusual hour
            var currentHour = DateTime.UtcNow.Hour;
            bool isUnusualHour = currentHour >= 1 && currentHour <= 5;

            if (isUnusualHour)
            {
                await RecordRiskEventAsync(userId, "TxUnusualHour", 10, JsonSerializer.Serialize(new { amount, hour = currentHour }));
            }

            if (!string.IsNullOrWhiteSpace(recipientInfo))
            {
                bool isNewPayee = true;
                if (int.TryParse(recipientInfo, out var targetAccId))
                {
                    isNewPayee = !await _context.Transactions
                        .AnyAsync(t => accountIds.Contains(t.AccountId) && t.RelatedAccountId == targetAccId);
                }
                else
                {
                    var targetAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.AccountNumber == recipientInfo);
                    if (targetAccount != null)
                    {
                        isNewPayee = !await _context.Transactions
                            .AnyAsync(t => accountIds.Contains(t.AccountId) && t.RelatedAccountId == targetAccount.Id);
                    }
                }

                if (isNewPayee)
                {
                    await RecordRiskEventAsync(userId, "TxNewPayee", 10, JsonSerializer.Serialize(new { payee = recipientInfo, amount }));
                }
            }

            // Check multiple large tx within 1 hour
            var oneHourAgo = DateTime.UtcNow.AddHours(-1);
            var recentLargeTxs = pastTxs.Count(t => t.Timestamp >= oneHourAgo && t.Amount > 2 * avgAmount);
            if (recentLargeTxs >= 2)
            {
                await RecordRiskEventAsync(userId, "MultipleLargeTxInOneHour", 20, JsonSerializer.Serialize(new { count = recentLargeTxs, amount }));
            }

            if (!isLarge && !isVeryLarge)
            {
                result.StepUpRequired = false;
                result.TransactionAllowed = true;
                return result;
            }

            // Step-Up required for Large / Very Large transaction
            result.StepUpRequired = true;

            bool isVaultPasswordValid = !string.IsNullOrEmpty(user.VaultPasswordHash) &&
                                        !string.IsNullOrWhiteSpace(providedVaultPassword) &&
                                        PasswordHasher.VerifyPassword(providedVaultPassword.Trim(), user.VaultPasswordHash);

            if (isVaultPasswordValid)
            {
                // Correct Vault Password: No penalty, transaction proceeds, log for admin visibility
                result.StepUpPassed = true;
                result.TransactionAllowed = true;
                result.Message = "Vault password step-up verification succeeded.";

                await RecordRiskEventAsync(userId, "LargeTxStepUpPassed", 0, JsonSerializer.Serialize(new
                {
                    amount,
                    severity = result.Severity,
                    avg30Day = avgAmount
                }));
            }
            else
            {
                // Wrong or NOT provided Vault Password: Apply penalty (+25 for Large, +40 for Very Large) and BLOCK transaction
                int penalty = isVeryLarge ? 40 : 25;
                result.StepUpPassed = false;
                result.TransactionAllowed = false;
                result.Message = $"Transaction of ৳{amount:N2} requires Security Vault Password step-up verification. Transaction blocked.";

                await RecordRiskEventAsync(userId, isVeryLarge ? "VeryLargeTxNoVaultPass" : "LargeTxNoVaultPass", penalty, JsonSerializer.Serialize(new
                {
                    amount,
                    severity = result.Severity,
                    avg30Day = avgAmount,
                    blocked = true
                }));
            }

            return result;
        }
    }
}
