using System;
using System.Collections.Generic;
using SmartBank.DTOs.Loans;

namespace SmartBank.Services.Interfaces
{
    public interface ILoanCalculatorService
    {
        decimal CalculateEmi(decimal principal, decimal annualRate, int tenureMonths);
        decimal CalculateTotalRepayable(decimal emi, int tenureMonths);
        List<InstallmentScheduleItem> GenerateSchedule(decimal principal, decimal annualRate, int tenureMonths, DateTime firstDueDate);
        decimal CalculateDti(decimal emi, decimal monthlyIncome);
    }
}
