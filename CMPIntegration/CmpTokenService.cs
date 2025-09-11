using System.Net.Http.Headers;
using System.Text.Json;

namespace alloy_events_test.CMPIntegration
{
    public interface ICmpTokenService
    {
        Task<string> GetAccessTokenAsync();
        Task<CmpTokenResult> RefreshTokenAsync();
    }

    public class CmpTokenResult
    {
        public bool Success { get; set; }
        public string AccessToken { get; set; }
        public string TokenType { get; set; }
        public int ExpiresIn { get; set; }
        public DateTime ExpiresAt { get; set; }
        public string ErrorMessage { get; set; }
        public string ErrorDetails { get; set; }
    }

    public class CmpTokenService : ICmpTokenService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<CmpTokenService> _logger;
        private readonly IConfiguration _config;
        private static CmpTokenResult _cachedToken;
        private static readonly SemaphoreSlim _tokenSemaphore = new(1, 1);

        public CmpTokenService(
            HttpClient httpClient,
            ILogger<CmpTokenService> logger,
            IConfiguration config)
        {
            _httpClient = httpClient;
            _logger = logger;
            _config = config;
        }

        public async Task<string> GetAccessTokenAsync()
        {
            // Check if we have a valid cached token
            if (_cachedToken != null && _cachedToken.Success && _cachedToken.ExpiresAt > DateTime.UtcNow.AddMinutes(5))
            {
                _logger.LogDebug("🔄 Using cached CMP access token (expires at {ExpiresAt})", _cachedToken.ExpiresAt);
                return _cachedToken.AccessToken;
            }

            // Token is expired or doesn't exist, get a new one
            var tokenResult = await RefreshTokenAsync();
            
            if (tokenResult.Success)
            {
                return tokenResult.AccessToken;
            }

            // If OAuth fails, fall back to appsettings token
            _logger.LogWarning("⚠️ Failed to get CMP access token via OAuth, falling back to appsettings token");
            return _config.GetValue<string>("CmpSettings:AccessToken");
        }

        public async Task<CmpTokenResult> RefreshTokenAsync()
        {
            await _tokenSemaphore.WaitAsync();
            
            try
            {
                // Double-check if another thread already refreshed the token
                if (_cachedToken != null && _cachedToken.Success && _cachedToken.ExpiresAt > DateTime.UtcNow.AddMinutes(5))
                {
                    _logger.LogDebug("🔄 Another thread already refreshed the token");
                    return _cachedToken;
                }

                _logger.LogInformation("🔄 Requesting new CMP access token via OAuth2 client credentials flow");

                var clientId = _config.GetValue<string>("CmpSettings:ClientId");
                var clientSecret = _config.GetValue<string>("CmpSettings:ClientSecret");
                var tokenUrl = GetTokenUrl();

                if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
                {
                    _logger.LogError("❌ CMP ClientId or ClientSecret not configured");
                    return new CmpTokenResult
                    {
                        Success = false,
                        ErrorMessage = "CMP ClientId or ClientSecret not configured",
                        ErrorDetails = "Please configure CmpSettings:ClientId and CmpSettings:ClientSecret in appsettings.json"
                    };
                }

                var requestPayload = new
                {
                    client_id = clientId,
                    client_secret = clientSecret,
                    grant_type = "client_credentials"
                };

                var json = JsonSerializer.Serialize(requestPayload, new JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });
                
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                _logger.LogDebug("🔄 Making OAuth2 token request to {TokenUrl}", tokenUrl);
                _logger.LogTrace("OAuth2 request payload: {Payload}", json);

                var response = await _httpClient.PostAsync(tokenUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    _logger.LogTrace("OAuth2 response: {Response}", responseContent);

                    var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });

                    if (tokenResponse != null && !string.IsNullOrEmpty(tokenResponse.AccessToken))
                    {
                        var expiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 30); // 30 seconds buffer
                        
                        var result = new CmpTokenResult
                        {
                            Success = true,
                            AccessToken = tokenResponse.AccessToken,
                            TokenType = tokenResponse.TokenType ?? "Bearer",
                            ExpiresIn = tokenResponse.ExpiresIn,
                            ExpiresAt = expiresAt
                        };

                        // Cache the token
                        _cachedToken = result;

                        _logger.LogInformation("✅ Successfully obtained CMP access token (expires at {ExpiresAt})", expiresAt);
                        return result;
                    }
                    else
                    {
                        _logger.LogError("❌ Invalid token response: missing access_token");
                        return new CmpTokenResult
                        {
                            Success = false,
                            ErrorMessage = "Invalid token response",
                            ErrorDetails = "Response missing access_token field"
                        };
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    var statusCode = (int)response.StatusCode;
                    
                    var errorMessage = statusCode switch
                    {
                        400 => "Bad request - invalid client credentials or grant type",
                        401 => "Unauthorized - invalid client_id or client_secret",
                        403 => "Forbidden - client not authorized for client_credentials grant",
                        _ => $"Failed to obtain access token (HTTP {statusCode})"
                    };
                    
                    _logger.LogError("❌ Failed to get CMP access token. Status: {StatusCode}, Error: {Error}", 
                        response.StatusCode, errorContent);
                    
                    return new CmpTokenResult
                    {
                        Success = false,
                        ErrorMessage = errorMessage,
                        ErrorDetails = errorContent
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Exception while getting CMP access token");
                return new CmpTokenResult
                {
                    Success = false,
                    ErrorMessage = "Exception occurred while getting access token",
                    ErrorDetails = ex.Message
                };
            }
            finally
            {
                _tokenSemaphore.Release();
            }
        }

        private string GetTokenUrl()
        {
            var baseUrl = _config.GetValue<string>("CmpSettings:BaseUrl") ?? "https://api.cmp.optimizely.com/v3/";
            
            // Remove v3 from base URL and add OAuth endpoint
            if (baseUrl.EndsWith("/v3/"))
            {
                baseUrl = baseUrl.Replace("/v3/", "/");
            }
            
            return $"{baseUrl.TrimEnd('/')}/o/oauth2/v1/token";
        }
    }

    // Response model for OAuth2 token response
    internal class TokenResponse
    {
        public string AccessToken { get; set; }
        public string TokenType { get; set; }
        public int ExpiresIn { get; set; }
        public string Scope { get; set; }
    }
}