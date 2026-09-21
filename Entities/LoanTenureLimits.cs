using System;
using System.Collections.Generic;

namespace SmartBank.Entities
{
    public static class LoanTenureLimits
    {
        public static readonly Dictionary<string, (int MinMonths, int MaxMonths)> Limits = 
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "Emergency", (3, 12) },
                { "Personal", (6, 24) },
                { "Education", (12, 36) },
                { "Business", (6, 36) },
                { "Home", (24, 60) }
            };

        public static (int MinMonths, int MaxMonths) GetLimitsForType(string? loanType)
        {
            if (!string.IsNullOrWhiteSpace(loanType) && Limits.TryGetValue(loanType, out var limit))
            {
                return limit;
            }
            return (6, 24); // Default to personal
        }

        public static (int MinMonths, int MaxMonths) GetAllowedRange(string? loanType)
        {
            return GetLimitsForType(loanType);
        }
    }
}
