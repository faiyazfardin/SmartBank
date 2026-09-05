using System.Collections.Generic;
using System.Threading.Tasks;
using SmartBank.DTOs.Common;
using SmartBank.DTOs.Loans;

namespace SmartBank.Services.Interfaces
{
    public interface ILoanService
    {
        Task<(int StatusCode, ApiResponse<LoanApplicationDto> Response)> ApplyForLoanAsync(int userId, ApplyLoanRequest request);
        Task<List<LoanApplicationDto>> GetCustomerApplicationsAsync(int userId);
        Task<LoanApplicationDto?> GetApplicationByNumberAsync(int userId, string applicationNumber, bool isAdmin = false);
        Task<List<LoanApplicationDto>> GetAllApplicationsForAdminAsync(string? statusFilter = null);
        Task<(int StatusCode, ApiResponse<LoanApplicationDto> Response)> ReviewApplicationAsync(string applicationNumber, string adminUsername, bool isApprove, string comment);
        Task<AdminLoanStatsDto> GetAdminLoanStatsAsync();
    }
}
