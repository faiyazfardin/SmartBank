using System;
using System.Collections.Generic;
using SmartBank.DTOs.Loans;
using SmartBank.Services.Interfaces;

namespace SmartBank.Services
{
    public class LoanCalculatorService : ILoanCalculatorService
    {
        public decimal CalculateEmi(decimal principal, decimal annualRate, int tenureMonths)
        {
            if (principal <= 0 || tenureMonths <= 0) return 0m;

            if (annualRate <= 0m)
            {
                return Math.Round(principal / tenureMonths, 2);
            }

            // Reducing balance EMI formula:
            // r = annualRate / 12 / 100
            // EMI = P * r * (1+r)^n / ((1+r)^n - 1)
            double r = (double)(annualRate / 12m / 100m);
            double n = tenureMonths;
            double pow = Math.Pow(1.0 + r, n);
            double emiDouble = (double)principal * (r * pow) / (pow - 1.0);

            return Math.Round((decimal)emiDouble, 2);
        }

        public decimal CalculateTotalRepayable(decimal emi, int tenureMonths)
        {
            if (emi <= 0 || tenureMonths <= 0) return 0m;
            return Math.Round(emi * tenureMonths, 2);
        }

        public List<InstallmentScheduleItem> GenerateSchedule(decimal principal, decimal annualRate, int tenureMonths, DateTime firstDueDate)
        {
            var schedule = new List<InstallmentScheduleItem>();
            if (principal <= 0 || tenureMonths <= 0) return schedule;

            decimal emi = CalculateEmi(principal, annualRate, tenureMonths);
            decimal monthlyRate = annualRate / 12m / 100m;
            decimal currentBalance = principal;

            for (int i = 1; i <= tenureMonths; i++)
            {
                decimal openingBalance = currentBalance;
                decimal interestPortion = Math.Round(openingBalance * monthlyRate, 2);
                decimal principalPortion;
                decimal totalDue;
                decimal closingBalance;

                // Adjust last installment to absorb any floating point / rounding drift
                if (i == tenureMonths)
                {
                    principalPortion = openingBalance;
                    totalDue = principalPortion + interestPortion;
                    closingBalance = 0.00m;
                }
                else
                {
                    principalPortion = emi - interestPortion;
                    if (principalPortion > openingBalance)
                    {
                        principalPortion = openingBalance;
                    }
                    totalDue = principalPortion + interestPortion;
                    closingBalance = Math.Max(0.00m, openingBalance - principalPortion);
                }

                var dueDate = firstDueDate.AddMonths(i - 1);

                schedule.Add(new InstallmentScheduleItem
                {
                    InstallmentNumber = i,
                    DueDate = dueDate,
                    OpeningBalance = openingBalance,
                    PrincipalPortion = principalPortion,
                    InterestPortion = interestPortion,
                    TotalDue = totalDue,
                    ClosingBalance = closingBalance
                });

                currentBalance = closingBalance;
            }

            return schedule;
        }

        public decimal CalculateDti(decimal emi, decimal monthlyIncome)
        {
            if (monthlyIncome <= 0) return -1m;
            if (emi <= 0) return 0m;
            return Math.Round((emi / monthlyIncome) * 100m, 2);
        }
    }
}
