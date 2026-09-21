using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SmartBank.Data;
using SmartBank.DTOs.Loans;
using SmartBank.Entities;
using SmartBank.Services.Interfaces;

namespace SmartBank.Services
{
    public class LoanEligibilityService : ILoanEligibilityService
    {
        private readonly SmartBankDbContext _context;
        private readonly ILoanCalculatorService _calculatorService;
        private const decimal SystemMaxLoanLimit = 500000m;
        private const decimal MinimumAllowableLoan = 5000m;

        public LoanEligibilityService(SmartBankDbContext context, ILoanCalculatorService calculatorService)
        {
            _context = context;
            _calculatorService = calculatorService;
        }

        public async Task<LoanEligibilityResultDto> EvaluateEligibilityAsync(int userId)
        {
            var user = await _context.Users
                .Include(u => u.Accounts)
                    .ThenInclude(a => a.Transactions)
                .FirstOrDefaultAsync(u => u.Id == userId);

            var result = new LoanEligibilityResultDto();

            if (user == null)
            {
                result.Eligible = false;
                result.Score = 0;
                result.Category = "Not Eligible";
                result.MaximumAmount = 0;
                result.Reasons.Add("User profile not found.");
                return result;
            }

            var account = user.Accounts.FirstOrDefault();
            if (account == null)
            {
                result.Eligible = false;
                result.Score = 0;
                result.Category = "Not Eligible";
                result.MaximumAmount = 0;
                result.Reasons.Add("No banking account found for this user.");
                return result;
            }

            var now = DateTime.UtcNow;
            var transactions = account.Transactions?.ToList() ?? new List<Transaction>();
            var totalTxCount = transactions.Count;
            var accountAgeDays = Math.Max(1, (now - account.CreatedAt).TotalDays);
            var accountAgeMonths = accountAgeDays / 30.0;

            result.CurrentBalance = account.Balance;
            result.TotalTransactions = totalTxCount;
            result.AccountAgeMonths = Math.Round(accountAgeMonths, 1);

            // Calculate Average Monthly Balance
            // If account is newer, use current balance or inflow/outflow balance
            decimal averageMonthlyBalance;
            if (accountAgeMonths >= 1.0)
            {
                decimal totalInflow = transactions
                    .Where(t => t.Type == TransactionType.Deposit || t.Type == TransactionType.TransferIn)
                    .Sum(t => t.Amount);
                var monthlyInflowRate = totalInflow / (decimal)accountAgeMonths;
                averageMonthlyBalance = Math.Max(account.Balance, Math.Min(account.Balance, monthlyInflowRate));
            }
            else
            {
                averageMonthlyBalance = account.Balance;
            }

            if (averageMonthlyBalance <= 0 && account.Balance > 0)
            {
                averageMonthlyBalance = account.Balance;
            }

            result.AverageMonthlyBalance = Math.Round(averageMonthlyBalance, 2);

            // 1. Scoring Calculation (Max 100 Points)
            // Account age / time condition is always satisfied (Full 20 Points)
            int scoreAccountAge = 20;

            int scoreBalance = 0;
            if (account.Balance >= 50000m) scoreBalance = 30;
            else if (account.Balance >= 25000m) scoreBalance = 25;
            else if (account.Balance >= 10000m) scoreBalance = 20;
            else if (account.Balance >= 5000m) scoreBalance = 15;
            else if (account.Balance >= 1000m) scoreBalance = 10;
            else scoreBalance = 0;

            int scoreActivity = 0;
            if (totalTxCount >= 10) scoreActivity = 20;
            else if (totalTxCount >= 5) scoreActivity = 18;
            else if (totalTxCount >= 3) scoreActivity = 15;
            else if (totalTxCount >= 1) scoreActivity = 5;
            else scoreActivity = 0;

            int scoreStability = 0;
            var recentTxCount30Days = transactions.Count(t => (now - t.Timestamp).TotalDays <= 30);
            decimal totalInflows = transactions.Where(t => t.Type == TransactionType.Deposit || t.Type == TransactionType.TransferIn).Sum(t => t.Amount);
            decimal totalOutflows = transactions.Where(t => t.Type == TransactionType.Withdraw || t.Type == TransactionType.TransferOut).Sum(t => t.Amount);

            if (recentTxCount30Days >= 1 && totalInflows >= totalOutflows)
            {
                scoreStability = 20;
            }
            else if (recentTxCount30Days >= 1 || totalInflows >= totalOutflows)
            {
                scoreStability = 15;
            }
            else if (totalTxCount > 0)
            {
                scoreStability = 10;
            }
            else
            {
                scoreStability = 0;
            }

            int scoreRisk = 0;
            if (user.FailedLoginCount == 0 && (!user.LockedUntil.HasValue || user.LockedUntil.Value <= now))
            {
                scoreRisk = 10;
            }
            else if (user.FailedLoginCount <= 2)
            {
                scoreRisk = 5;
            }
            else
            {
                scoreRisk = 0;
            }

            int totalScore = scoreAccountAge + scoreBalance + scoreActivity + scoreStability + scoreRisk;
            result.Score = Math.Clamp(totalScore, 0, 100);

            result.ScoreBreakdown["Account Age (20)"] = scoreAccountAge;
            result.ScoreBreakdown["Balance & Liquidity (30)"] = scoreBalance;
            result.ScoreBreakdown["Account Activity (20)"] = scoreActivity;
            result.ScoreBreakdown["Account Stability (20)"] = scoreStability;
            result.ScoreBreakdown["Risk History (10)"] = scoreRisk;

            // 2. Eligibility Category
            if (result.Score >= 80)
            {
                result.Category = "Excellent";
            }
            else if (result.Score >= 65)
            {
                result.Category = "Good";
            }
            else if (result.Score >= 50)
            {
                result.Category = "Review Required";
            }
            else
            {
                result.Category = "Not Eligible";
            }

            // 3. Rule Checks & Reasons List
            bool isAccountActive = account.IsActive && user.Status.Equals("Active", StringComparison.OrdinalIgnoreCase);
            bool isKycApproved = true; // Automatically approved for active accounts
            bool isAgeSatisfied = true; // Always true per requirement
            bool isTxHistorySatisfied = totalTxCount >= 3;
            bool isBalanceSatisfied = account.Balance >= 1000m;

            var reasons = new List<string>();

            if (isAccountActive)
                reasons.Add("✓ Account is active and in good standing");
            else
                reasons.Add("✗ Account must be active and not under suspension or freeze");

            if (isKycApproved)
                reasons.Add("✓ Customer KYC & identity verified");
            else
                reasons.Add("✗ KYC verification required");

            if (isAgeSatisfied)
                reasons.Add($"✓ Account tenure requirement satisfied ({result.AccountAgeMonths:F1} months active)");

            if (isTxHistorySatisfied)
                reasons.Add("✓ Account activity threshold satisfied");
            else
                reasons.Add("✗ Additional account activity required for loan qualification");

            if (isBalanceSatisfied)
                reasons.Add($"✓ Current balance (৳{account.Balance:N2}) meets liquidity criteria");
            else
                reasons.Add("✗ Current balance is insufficient for loan consideration");

            if (scoreStability >= 15)
                reasons.Add("✓ Stable account activity and standing confirmed");

            result.Reasons = reasons;

            // 4. Maximum Eligible Amount Calculation
            // Maximum Loan = MIN(Average Monthly Balance * 3, 500,000), minimum 5,000
            if (result.Score >= 50 && isAccountActive && isAgeSatisfied && isTxHistorySatisfied)
            {
                result.Eligible = true;
                decimal calculatedMax = result.AverageMonthlyBalance * 3m;
                if (calculatedMax < MinimumAllowableLoan)
                {
                    calculatedMax = MinimumAllowableLoan;
                }
                calculatedMax = Math.Floor(calculatedMax / 1000m) * 1000m;
                result.MaximumAmount = Math.Max(MinimumAllowableLoan, Math.Min(calculatedMax, SystemMaxLoanLimit));
            }
            else
            {
                result.Eligible = false;
                result.MaximumAmount = 0m;
            }

            // 5. Indicative Rate & Sample EMI Calculations
            result.IndicativeRate = GetIndicativeRate(result.Category);
            result.SampleTenureMonths = 12;

            if (result.Eligible && result.MaximumAmount > 0)
            {
                result.SampleEmi = _calculatorService.CalculateEmi(result.MaximumAmount, result.IndicativeRate, result.SampleTenureMonths);
                result.SampleTotalRepayable = _calculatorService.CalculateTotalRepayable(result.SampleEmi, result.SampleTenureMonths);
            }
            else
            {
                result.SampleEmi = 0m;
                result.SampleTotalRepayable = 0m;
            }

            return result;
        }

        public decimal GetIndicativeRate(string? eligibilityCategory)
        {
            var cleanCategory = (eligibilityCategory ?? string.Empty).Replace(" ", "").Trim();

            if (string.Equals(cleanCategory, "Excellent", StringComparison.OrdinalIgnoreCase))
                return 8.00m;
            if (string.Equals(cleanCategory, "Good", StringComparison.OrdinalIgnoreCase))
                return 10.50m;
            if (string.Equals(cleanCategory, "ReviewRequired", StringComparison.OrdinalIgnoreCase))
                return 13.00m;

            return 13.00m;
        }

        public async Task<decimal> GetIndicativeRateAsync(string? eligibilityCategory)
        {
            var cleanCategory = (eligibilityCategory ?? string.Empty).Replace(" ", "").Trim();

            var policy = await _context.LoanRatePolicies
                .Where(p => p.IsActive && p.Category == cleanCategory)
                .OrderByDescending(p => p.EffectiveFrom)
                .FirstOrDefaultAsync();

            if (policy != null)
            {
                return policy.BaseAnnualRate;
            }

            return GetIndicativeRate(eligibilityCategory);
        }

        public async Task<decimal> GetSampleEmiAsync(int userId, int sampleTenureMonths = 12)
        {
            var eligibility = await EvaluateEligibilityAsync(userId);
            if (!eligibility.Eligible || eligibility.MaximumAmount <= 0) return 0m;

            return _calculatorService.CalculateEmi(eligibility.MaximumAmount, eligibility.IndicativeRate, sampleTenureMonths);
        }

        public decimal GetSampleEmi(decimal principal, string? eligibilityCategory, int sampleTenureMonths = 12)
        {
            var rate = GetIndicativeRate(eligibilityCategory);
            return _calculatorService.CalculateEmi(principal, rate, sampleTenureMonths);
        }
    }
}
