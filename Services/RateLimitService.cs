using System;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using SmartBank.Services.Interfaces;

namespace SmartBank.Services
{
    public class RateLimitService : IRateLimitService
    {
        private readonly IMemoryCache _cache;
        private readonly int _maxAttempts;
        private readonly int _periodMinutes;

        private class AttemptRecord
        {
            public int Count { get; set; }
            public DateTime FirstAttemptUtc { get; set; }
            public DateTime? BlockedUntilUtc { get; set; }
        }

        public RateLimitService(IMemoryCache cache, IConfiguration configuration)
        {
            _cache = cache;
            _maxAttempts = int.TryParse(configuration["Security:RateLimitAttempts"], out var max) ? max : 5;
            _periodMinutes = int.TryParse(configuration["Security:RateLimitPeriodMinutes"], out var period) ? period : 15;
        }

        public bool IsRateLimited(string ipAddress, out int retryAfterMinutes)
        {
            retryAfterMinutes = 0;
            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                return false;
            }

            var cacheKey = $"RateLimit_{ipAddress}";
            if (_cache.TryGetValue(cacheKey, out AttemptRecord? record) && record != null)
            {
                if (record.BlockedUntilUtc.HasValue && record.BlockedUntilUtc > DateTime.UtcNow)
                {
                    var remaining = record.BlockedUntilUtc.Value - DateTime.UtcNow;
                    retryAfterMinutes = (int)Math.Ceiling(remaining.TotalMinutes);
                    if (retryAfterMinutes <= 0) retryAfterMinutes = 1;
                    return true;
                }

                if (record.Count >= _maxAttempts)
                {
                    record.BlockedUntilUtc = DateTime.UtcNow.AddMinutes(_periodMinutes);
                    retryAfterMinutes = _periodMinutes;
                    _cache.Set(cacheKey, record, TimeSpan.FromMinutes(_periodMinutes));
                    return true;
                }
            }

            return false;
        }

        public void RecordFailedAttempt(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                return;
            }

            var cacheKey = $"RateLimit_{ipAddress}";
            if (!_cache.TryGetValue(cacheKey, out AttemptRecord? record) || record == null)
            {
                record = new AttemptRecord
                {
                    Count = 1,
                    FirstAttemptUtc = DateTime.UtcNow
                };
            }
            else
            {
                record.Count++;
                if (record.Count >= _maxAttempts)
                {
                    record.BlockedUntilUtc = DateTime.UtcNow.AddMinutes(_periodMinutes);
                }
            }

            _cache.Set(cacheKey, record, TimeSpan.FromMinutes(_periodMinutes));
        }

        public void ResetAttempts(string ipAddress)
        {
            if (!string.IsNullOrWhiteSpace(ipAddress))
            {
                var cacheKey = $"RateLimit_{ipAddress}";
                _cache.Remove(cacheKey);
            }
        }
    }
}
