using System.Threading.Tasks;
using SmartBank.DTOs.Loans;

namespace SmartBank.Services.Interfaces
{
    public interface ILoanEligibilityService
    {
        Task<LoanEligibilityResultDto> EvaluateEligibilityAsync(int userId);
        decimal GetIndicativeRate(string? eligibilityCategory);
        Task<decimal> GetIndicativeRateAsync(string? eligibilityCategory);
        Task<decimal> GetSampleEmiAsync(int userId, int sampleTenureMonths = 12);
        decimal GetSampleEmi(decimal principal, string? eligibilityCategory, int sampleTenureMonths = 12);
    }
}
