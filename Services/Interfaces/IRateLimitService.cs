namespace SmartBank.Services.Interfaces
{
    public interface IRateLimitService
    {
        bool IsRateLimited(string ipAddress, out int retryAfterMinutes);
        void RecordFailedAttempt(string ipAddress);
        void ResetAttempts(string ipAddress);
    }
}
