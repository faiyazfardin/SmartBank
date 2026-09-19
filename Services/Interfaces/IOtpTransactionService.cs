using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SmartBank.DTOs.Common;
using SmartBank.DTOs.Transactions;
using SmartBank.Entities;

namespace SmartBank.Services.Interfaces
{
    public interface IOtpTransactionService
    {
        /// <summary>
        /// Validates account state, creates PendingTransaction row, generates 6-digit OTP,
        /// hashes & stores in OtpChallenge (ExpiresAt = IssuedAt + 120s), and emails user.
        /// </summary>
        Task<(int StatusCode, ApiResponse<OtpChallengeResult> Response)> InitiateTransactionAsync(int userId, TransactionRequestDto request);

        /// <summary>
        /// Verifies 6-digit OTP against challenge, executes atomic money movement in single IDbContextTransaction,
        /// marks OTP used (IsUsed = true), marks PendingTransaction Completed, and dispatches confirmation email.
        /// </summary>
        Task<(int StatusCode, ApiResponse<OtpVerificationResult> Response)> VerifyOtpAndCommitAsync(Guid challengeId, string userEnteredOtp);

        /// <summary>
        /// Resends 6-digit OTP for challenge respecting 60-second cooldown per challenge.
        /// </summary>
        Task<(int StatusCode, ApiResponse<OtpChallengeResult> Response)> ResendOtpAsync(Guid challengeId);

        /// <summary>
        /// Background process to invalidate expired challenges and pending transactions past ExpiresAt.
        /// </summary>
        Task InvalidateExpiredChallengesAsync();

        /// <summary>
        /// Retrieves pending OTP-protected transactions for a given user.
        /// </summary>
        Task<List<PendingTransaction>> GetPendingTransactionsAsync(int userId);
    }
}
