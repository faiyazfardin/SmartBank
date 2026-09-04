using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SmartBank.Client.Configuration;
using SmartBank.Client.Exceptions;
using SmartBank.Client.Models.Auth;
using SmartBank.Client.Models.Common;
using SmartBank.Client.Security;

namespace SmartBank.Client.Services
{
    public class ApiClient
    {
        private static readonly Lazy<HttpClient> _httpClientLazy = new(() =>
        {
            var handler = new HttpClientHandler
            {
                // In local dev environments, allow self-signed dev certificates for https://localhost
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };
            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        });

        private static HttpClient Client => _httpClientLazy.Value;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static readonly string[] CandidateBaseUrls = new[]
        {
            "http://localhost:5096/api/",
            "https://localhost:7164/api/"
        };

        private static string _activeBaseUrl = CandidateBaseUrls[0];

        private static string GetEndpointUrl(string endpoint, string? baseUrl = null)
        {
            var baseUri = (baseUrl ?? _activeBaseUrl).TrimEnd('/') + "/";
            return baseUri + endpoint.TrimStart('/');
        }

        private static void SetAuthorizationHeader(HttpRequestMessage request)
        {
            var token = SessionManager.Instance.Token;
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }

        public static async Task<T> SendAsync<T>(HttpMethod method, string endpoint, object? body = null, bool retryOn401 = true)
        {
            HttpResponseMessage? response = null;
            Exception? lastEx = null;

            // Try the known active URL first for maximum performance
            var urlsToTry = new List<string> { _activeBaseUrl };
            foreach (var url in CandidateBaseUrls)
            {
                if (!urlsToTry.Contains(url))
                {
                    urlsToTry.Add(url);
                }
            }

            foreach (var candidateUrl in urlsToTry)
            {
                var targetUrl = GetEndpointUrl(endpoint, candidateUrl);
                using var request = new HttpRequestMessage(method, targetUrl);
                SetAuthorizationHeader(request);

                if (body != null)
                {
                    var jsonBody = JsonSerializer.Serialize(body, JsonOptions);
                    request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                }

                try
                {
                    response = await Client.SendAsync(request);
                    _activeBaseUrl = candidateUrl; // Lock in successful working URL
                    break;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                }
            }

            if (response == null)
            {
                throw new ApiException(
                    0,
                    "Backend API is not running.\n\nPlease start the backend API first in another terminal:\ndotnet run --project SmartBank.csproj",
                    new List<string> { "Please start the backend API first: dotnet run --project SmartBank.csproj" });
            }

            // Handle 401 Unauthorized with token refresh attempt
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && retryOn401 && !string.IsNullOrEmpty(SessionManager.Instance.RefreshToken))
            {
                var refreshSuccess = await TryRefreshTokenAsync();
                if (refreshSuccess)
                {
                    // Retry original request once
                    return await SendAsync<T>(method, endpoint, body, retryOn401: false);
                }
                else
                {
                    SessionManager.Instance.ClearSession();
                }
            }

            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                int? retryAfterSeconds = null;
                if (response.Headers.RetryAfter?.Delta.HasValue == true)
                {
                    retryAfterSeconds = (int)response.Headers.RetryAfter.Delta.Value.TotalSeconds;
                }

                string errorMessage = $"Request failed with status code {(int)response.StatusCode}";
                List<string> errors = new();

                try
                {
                    var errorObj = JsonSerializer.Deserialize<ApiResponse<object>>(content, JsonOptions);
                    if (errorObj != null)
                    {
                        if (!string.IsNullOrEmpty(errorObj.Message))
                        {
                            errorMessage = errorObj.Message;
                        }
                        if (errorObj.Errors != null && errorObj.Errors.Count > 0)
                        {
                            errors.AddRange(errorObj.Errors);
                        }
                    }
                }
                catch
                {
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        errors.Add(content);
                    }
                }

                throw new ApiException((int)response.StatusCode, errorMessage, errors, retryAfterSeconds);
            }

            if (typeof(T) == typeof(string))
            {
                return (T)(object)content;
            }

            try
            {
                var result = JsonSerializer.Deserialize<T>(content, JsonOptions);
                if (result == null)
                {
                    throw new ApiException((int)response.StatusCode, "Failed to deserialize server response.");
                }
                return result;
            }
            catch (Exception ex) when (ex is not ApiException)
            {
                throw new ApiException((int)response.StatusCode, "Invalid JSON received from server.", new List<string> { ex.Message });
            }
        }

        private static async Task<bool> TryRefreshTokenAsync()
        {
            try
            {
                var token = SessionManager.Instance.Token;
                var refreshToken = SessionManager.Instance.RefreshToken;
                if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(refreshToken))
                {
                    return false;
                }

                var refreshUrl = GetEndpointUrl("auth/refresh");
                var body = new { token, refreshToken };
                var jsonBody = JsonSerializer.Serialize(body, JsonOptions);

                using var request = new HttpRequestMessage(HttpMethod.Post, refreshUrl)
                {
                    Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
                };

                var response = await Client.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var res = JsonSerializer.Deserialize<ApiResponse<TokenRefreshResponse>>(content, JsonOptions);
                    if (res?.Data != null)
                    {
                        SessionManager.Instance.UpdateToken(res.Data.Token, res.Data.RefreshToken, res.Data.ExpiresIn);
                        return true;
                    }
                }
            }
            catch
            {
                // Refresh failed
            }

            return false;
        }

        public static Task<T> GetAsync<T>(string endpoint) => SendAsync<T>(HttpMethod.Get, endpoint);
        public static Task<T> PostAsync<T>(string endpoint, object? body = null) => SendAsync<T>(HttpMethod.Post, endpoint, body);
        public static Task<T> PutAsync<T>(string endpoint, object? body = null) => SendAsync<T>(HttpMethod.Put, endpoint, body);
        public static Task<T> DeleteAsync<T>(string endpoint) => SendAsync<T>(HttpMethod.Delete, endpoint);
    }
}
