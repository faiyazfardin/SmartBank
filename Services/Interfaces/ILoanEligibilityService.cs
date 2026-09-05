using System.Threading.Tasks;
using SmartBank.DTOs.Loans;

namespace SmartBank.Services.Interfaces
{
    public interface ILoanEligibilityService
    {
        Task<LoanEligibilityResultDto> EvaluateEligibilityAsync(int userId);
    }
}
