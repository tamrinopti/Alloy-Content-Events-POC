using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace alloy_events_test.CMPIntegration
{
    public interface ICmpTaskAssetService
    {
        Task<CmpTaskAssetResult> AttachStructuredContentToTaskAsync(string taskId, string contentId);
    }

    public class CmpTaskAssetService : ICmpTaskAssetService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<CmpTaskAssetService> _logger;
        private readonly ICmpTokenService _cmpTokenService;
        private readonly IConfiguration _config;

        public CmpTaskAssetService(
            IHttpClientFactory httpClientFactory,
            ILogger<CmpTaskAssetService> logger,
            ICmpTokenService cmpTokenService,
            IConfiguration config)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _cmpTokenService = cmpTokenService;
            _config = config;
        }

        public async Task<CmpTaskAssetResult> AttachStructuredContentToTaskAsync(string taskId, string contentId)
        {
            try
            {
                _logger.LogInformation("🔗 Attaching structured content {ContentId} to task {TaskId}", contentId, taskId);

                var accessToken = await _cmpTokenService.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(accessToken))
                {
                    return new CmpTaskAssetResult
                    {
                        Success = false,
                        ErrorMessage = "Failed to obtain access token"
                    };
                }

                var httpClient = _httpClientFactory.CreateClient();
                var baseUrl = _config.GetValue<string>("CmpSettings:BaseUrl") ?? "https://api.cmp.optimizely.com/v3/";
                var requestUrl = $"{baseUrl.TrimEnd('/')}/tasks/{taskId}/assets";

                // Create request payload
                var requestPayload = new CmpTaskAssetRequest
                {
                    ContentType = "structured_content",
                    Type = "fork_from_library",
                    ContentId = contentId
                };

                var json = JsonSerializer.Serialize(requestPayload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });

                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Set authorization header
                httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

                _logger.LogDebug("🔗 Making request to attach asset: {RequestUrl}", requestUrl);
                _logger.LogTrace("Request payload: {Payload}", json);

                var response = await httpClient.PostAsync(requestUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    _logger.LogTrace("Response content: {Response}", responseContent);

                    var taskAsset = JsonSerializer.Deserialize<CmpTaskAssetResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                    });

                    if (taskAsset != null)
                    {
                        _logger.LogInformation("✅ Successfully attached structured content {ContentId} to task {TaskId}. Asset ID: {AssetId}", 
                            contentId, taskId, taskAsset.Id);

                        return new CmpTaskAssetResult
                        {
                            Success = true,
                            AssetId = taskAsset.Id,
                            Title = taskAsset.Title,
                            Type = taskAsset.Type,
                            CreatedAt = taskAsset.CreatedAt,
                            TaskAsset = taskAsset
                        };
                    }
                    else
                    {
                        _logger.LogError("❌ Invalid response: failed to deserialize task asset response");
                        return new CmpTaskAssetResult
                        {
                            Success = false,
                            ErrorMessage = "Invalid response format"
                        };
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    var statusCode = (int)response.StatusCode;

                    var errorMessage = statusCode switch
                    {
                        400 => "Bad request - invalid task ID or content ID",
                        401 => "Unauthorized - invalid or expired access token",
                        403 => "Forbidden - insufficient permissions",
                        404 => "Not found - task or content not found",
                        422 => "Unprocessable entity - invalid request data",
                        _ => $"Failed to attach structured content (HTTP {statusCode})"
                    };

                    _logger.LogError("❌ Failed to attach structured content to task. Status: {StatusCode}, Error: {Error}", 
                        response.StatusCode, errorContent);

                    return new CmpTaskAssetResult
                    {
                        Success = false,
                        ErrorMessage = errorMessage,
                        ErrorDetails = errorContent
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Exception while attaching structured content {ContentId} to task {TaskId}", contentId, taskId);
                return new CmpTaskAssetResult
                {
                    Success = false,
                    ErrorMessage = "Exception occurred while attaching structured content",
                    ErrorDetails = ex.Message
                };
            }
        }
    }

    // Request model
    public class CmpTaskAssetRequest
    {
        [JsonPropertyName("content_type")]
        public string ContentType { get; set; } = "structured_content";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "fork_from_library";

        [JsonPropertyName("content_id")]
        public string ContentId { get; set; }
    }

    // Response models
    public class CmpTaskAssetResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("mime_type")]
        public string MimeType { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("modified_at")]
        public DateTime ModifiedAt { get; set; }

        [JsonPropertyName("labels")]
        public List<CmpTaskAssetLabel> Labels { get; set; } = new();

        [JsonPropertyName("content")]
        public CmpTaskAssetContent Content { get; set; }

        [JsonPropertyName("links")]
        public CmpTaskAssetLinks Links { get; set; }
    }

    public class CmpTaskAssetLabel
    {
        [JsonPropertyName("group")]
        public string Group { get; set; }

        [JsonPropertyName("value")]
        public string Value { get; set; }
    }

    public class CmpTaskAssetContent
    {
        [JsonPropertyName("url")]
        public string Url { get; set; }

        [JsonPropertyName("size")]
        public long? Size { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("value")]
        public string Value { get; set; }
    }

    public class CmpTaskAssetLinks
    {
        [JsonPropertyName("self")]
        public string Self { get; set; }

        [JsonPropertyName("task")]
        public string Task { get; set; }

        [JsonPropertyName("drafts")]
        public string Drafts { get; set; }

        [JsonPropertyName("web_urls")]
        public CmpTaskAssetWebUrls WebUrls { get; set; }
    }

    public class CmpTaskAssetWebUrls
    {
        [JsonPropertyName("self")]
        public string Self { get; set; }

        [JsonPropertyName("task")]
        public string Task { get; set; }

        [JsonPropertyName("view")]
        public string View { get; set; }

        [JsonPropertyName("drafts")]
        public string Drafts { get; set; }
    }

    // Result model
    public class CmpTaskAssetResult
    {
        public bool Success { get; set; }
        public string AssetId { get; set; }
        public string Title { get; set; }
        public string Type { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string ErrorMessage { get; set; }
        public string ErrorDetails { get; set; }
        public CmpTaskAssetResponse TaskAsset { get; set; }
    }
}