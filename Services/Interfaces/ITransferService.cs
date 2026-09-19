using System;
using System.Threading.Tasks;
using SmartBank.DTOs.Common;
using SmartBank.Entities;

namespace SmartBank.Services.Interfaces
{
    public class InitiateTransferResult
    {
        public int TransferRequestId { get; set; }
        public decimal Amount { get; set; }
        public string RecipientAccountNumber { get; set; } = string.Empty;
        public string MaskedRecipient { get; set; } = string.Empty;
        public string MaskedEmail { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
    }

    public class CompleteTransferResult
    {
        public int TransferRequestId { get; set; }
        public decimal Amount { get; set; }
        public decimal SenderNewBalance { get; set; }
        public string RecipientAccountNumber { get; set; } = string.Empty;
        public string TransactionId { get; set; } = string.Empty;
        public DateTime CompletedAt { get; set; }
    }

    public interface ITransferService
    {
        Task<(int StatusCode, ApiResponse<InitiateTransferResult> Response)> InitiateTransferAsync(int userId, string recipientAccountNumber, decimal amount, string? memo);
        Task<(int StatusCode, ApiResponse<CompleteTransferResult> Response)> VerifyAndCompleteTransferAsync(int userId, int transferRequestId, string otp);
        Task<(int StatusCode, ApiResponse<InitiateTransferResult> Response)> ResendTransferOtpAsync(int userId, int transferRequestId);
        Task<TransferRequest?> GetPendingTransferAsync(int userId, int transferRequestId);
    }
}
