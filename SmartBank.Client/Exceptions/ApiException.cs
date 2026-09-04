using System;
using System.Collections.Generic;

namespace SmartBank.Client.Exceptions
{
    public class ApiException : Exception
    {
        public int StatusCode { get; }
        public List<string> Errors { get; }
        public int? RetryAfterSeconds { get; }

        public ApiException(int statusCode, string message, List<string>? errors = null, int? retryAfterSeconds = null)
            : base(message)
        {
            StatusCode = statusCode;
            Errors = errors ?? new List<string>();
            RetryAfterSeconds = retryAfterSeconds;
        }
    }
}
