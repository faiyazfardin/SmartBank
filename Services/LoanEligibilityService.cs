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
        private const decimal SystemMaxLoanLimit = 500000m;
        private const decimal MinimumAllowableLoan = 5000m;

        public LoanEligibilityService(SmartBankDbContext context)
        {
            _context = context;
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
            int scoreAccountAge = 0;
            if (accountAgeMonths >= 12.0) scoreAccountAge = 20;
            else if (accountAgeMonths >= 6.0) scoreAccountAge = 15;
            else if (accountAgeMonths >= 3.0) scoreAccountAge = 10;
            else if (accountAgeMonths >= 1.0) scoreAccountAge = 5;
            else scoreAccountAge = 0;

            int scoreBalance = 0;
            if (account.Balance >= 50000m) scoreBalance = 30;
            else if (account.Balance >= 25000m) scoreBalance = 25;
            else if (account.Balance >= 10000m) scoreBalance = 20;
            else if (account.Balance >= 5000m) scoreBalance = 15;
            else if (account.Balance >= 1000m) scoreBalance = 10;
            else scoreBalance = 0;

            int scoreActivity = 0;
            if (totalTxCount >= 20) scoreActivity = 20;
            else if (totalTxCount >= 10) scoreActivity = 15;
            else if (totalTxCount >= 5) scoreActivity = 10;
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
            result.ScoreBreakdown["Transaction Activity (20)"] = scoreActivity;
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
            bool isKycApproved = !string.IsNullOrWhiteSpace(user.NidNumber);
            bool isAgeSatisfied = accountAgeMonths >= 6.0;
            bool isTxHistorySatisfied = totalTxCount >= 10;
            bool isBalanceSatisfied = account.Balance >= 1000m;

            var reasons = new List<string>();

            if (isAccountActive)
                reasons.Add("✓ Account is active and in good standing");
            else
                reasons.Add("✗ Account must be active and not under suspension or freeze");

            if (isKycApproved)
                reasons.Add("✓ Customer KYC & National ID verified");
            else
                reasons.Add("✗ KYC verification required (NID / Passport number missing)");

            if (isAgeSatisfied)
                reasons.Add($"✓ Account age ({result.AccountAgeMonths:F1} months) satisfies the 6-month minimum requirement");
            else
                reasons.Add($"✗ Account age ({result.AccountAgeMonths:F1} months) is less than the required 6 months");

            if (isTxHistorySatisfied)
                reasons.Add($"✓ Transaction volume ({totalTxCount} transactions) meets banking threshold");
            else
                reasons.Add($"✗ Insufficient transaction history ({totalTxCount}/10 required transactions)");

            if (isBalanceSatisfied)
                reasons.Add($"✓ Current balance (৳{account.Balance:N2}) meets liquidity criteria");
            else
                reasons.Add("✗ Current balance is insufficient for loan consideration");

            if (scoreStability >= 15)
                reasons.Add("✓ Stable cash flow and recent account activity confirmed");

            result.Reasons = reasons;

            // 4. Maximum Eligible Amount Calculation
            // Maximum Loan = MIN(Average Monthly Balance * 3, 500,000)
            if (result.Score >= 65 && isAccountActive && isKycApproved && isAgeSatisfied && isTxHistorySatisfied)
            {
                result.Eligible = true;
                decimal calculatedMax = Math.Min(result.AverageMonthlyBalance * 3m, SystemMaxLoanLimit);
                // Round down to nearest 5,000
                calculatedMax = Math.Floor(calculatedMax / 1000m) * 1000m;
                result.MaximumAmount = Math.Max(MinimumAllowableLoan, Math.Min(calculatedMax, SystemMaxLoanLimit));
            }
            else
            {
                result.Eligible = false;
                result.MaximumAmount = 0m;
            }

            return result;
        }
    }
}
