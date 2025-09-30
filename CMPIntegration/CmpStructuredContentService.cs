using EPiServer.Web.Routing;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using System.Linq;

namespace alloy_events_test.CMPIntegration
{

    // Interface for the CMP Structured Content Service
    public interface ICmpStructuredContentService
    {
        /// <summary>
        /// Creates structured content in CMP from Optimizely CMS content
        /// </summary>
        /// <param name="cmsContent">The CMS content to create structured content from</param>
        /// <param name="contentRef">Content reference of the CMS content</param>
        /// <param name="contentUrl">URL of the content</param>
        /// <param name="folderId">Optional folder ID to organize content in CMP</param>
        /// <returns>The CMP content ID of the created structured content</returns>
        Task<string> CreateAndPublishStructuredContentAsync(IContent cmsContent, ContentReference contentRef, string contentUrl, string folderId = null);
    }
    public class CmpStructuredContentService:ICmpStructuredContentService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<CmpStructuredContentService> _logger;
        private readonly ICmpTokenService _cmpTokenService;
        private readonly IUrlResolver _urlResolver;
        private readonly IConfiguration _config;
        private static readonly Dictionary<string, string> _contentTypeGuidCache = new();

        public CmpStructuredContentService(
            IHttpClientFactory httpClientFactory,
            ILogger<CmpStructuredContentService> logger,
            ICmpTokenService cmpTokenService,
            IUrlResolver urlResolver,
            IConfiguration config)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cmpTokenService = cmpTokenService ?? throw new ArgumentNullException(nameof(cmpTokenService));
            _config = config;
            _urlResolver = urlResolver;
        }

        public async Task<string> CreateAndPublishStructuredContentAsync(IContent cmsContent, ContentReference contentRef, string contentUrl, string folderId = null)
        {
            try
            {
                // Get content type information
                var contentType = cmsContent.GetOriginalType();
                var contentTypeName = contentType.Name;

                // Get content type GUID for CMP (check CMP API, create if needed)
                var contentTypeGuid = await GetContentTypeGuidForCmpAsync(contentTypeName, cmsContent);

                // Get published content details
                var publishedContent = cmsContent as IVersionable;
                var changeTrackable = cmsContent as IChangeTrackable;
                var localizable = cmsContent as ILocalizable;

                // Create the correct fields structure according to CMP API
                var fieldsData = CreateFieldsStructure(cmsContent, localizable?.Language?.Name ?? "en");

                // Create the content body according to the CMP API schema
                var contentBody = new
                {
                    content_type_guid = contentTypeGuid, // Required
                    root_content = true, // Required - indicates this is not embedded content
                    title = cmsContent.Name, // Title of the structured content
                    expired = publishedContent?.StopPublish < DateTime.Now, // Expired status
                    expiry_datetime = publishedContent?.StopPublish?.ToString("yyyy-MM-ddTHH:mm:ssZ"), // ISO 8601 format
                    source = "Optimizely CMS", // Source of the content
                    source_id = contentRef.ToString(), // Source id of the content
                    source_metadata = JsonSerializer.Serialize(new // Source metadata as JSON string
                    {
                        contentReference = contentRef.ToString(),
                        contentGuid = cmsContent.ContentGuid.ToString(),
                        contentTypeName = contentTypeName,
                        contentUrl = contentUrl,
                        publicUrl = GetPublicContentUrl(cmsContent),
                        publishedDate = publishedContent?.StartPublish?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        modifiedDate = changeTrackable?.Changed.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        language = localizable?.Language?.Name,
                        attachedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        cmsVersion = "Optimizely CMS 12"
                    }),
                    fields = fieldsData // Add the fields to the content body
                };

                // Create the full request payload according to LibraryStructuredContentPostRequest schema
                var requestPayload = new
                {
                    title = cmsContent.Name, // Title of the structured content asset
                    folder_id = folderId, // Optional: ID of the folder containing the structured content
                    content_body = contentBody // Required: The structured content body
                };

                // Serialize the payload
                var jsonPayload = JsonSerializer.Serialize(requestPayload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });

                // Post to CMP API
                var cmpResponse = await PostToCmpApiAsync(jsonPayload);

                if (cmpResponse.IsSuccess)
                {
                    _logger.LogInformation("Successfully created structured content in CMP for content {ContentRef}. CMP ID: {CmpId}",
                        contentRef, cmpResponse.ContentId);
                    return cmpResponse.ContentId;
                }
                else
                {
                    _logger.LogError("Failed to create structured content in CMP for content {ContentRef}. Error: {Error}",
                        contentRef, cmpResponse.ErrorMessage);
                    throw new CmpApiException($"Failed to create structured content in CMP: {cmpResponse.ErrorMessage}");
                }
            }
            catch (CmpApiException)
            {
                throw; // Re-throw CMP-specific exceptions
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while creating structured content in CMP for content {ContentRef}", contentRef);
                throw new CmpApiException($"Unexpected error while creating structured content in CMP: {ex.Message}", ex);
            }
        }

        private async Task<CmpApiResponse> PostToCmpApiAsync(string jsonPayload)
        {
            try
            {
                using var httpClient = _httpClientFactory.CreateClient("CmpApiClient");

                // Get access token
                var accessToken = await _cmpTokenService.GetAccessTokenAsync();

                // Set up the request
                var request = new HttpRequestMessage(HttpMethod.Post, "structured-contents")
                {
                    Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                };

                // Add authorization header
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                // Send the request
                var response = await httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var cmpContent = JsonSerializer.Deserialize<JsonElement>(responseContent);

                    // Extract the ID from the response according to LibraryStructuredContent schema
                    var contentId = cmpContent.GetProperty("id").GetString();

                    return new CmpApiResponse
                    {
                        IsSuccess = true,
                        ContentId = contentId,
                        ResponseContent = responseContent
                    };
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    var errorMessage = $"HTTP {(int)response.StatusCode} {response.StatusCode}";

                    try
                    {
                        var errorJson = JsonSerializer.Deserialize<JsonElement>(errorContent);
                        if (errorJson.TryGetProperty("message", out var messageProperty))
                        {
                            errorMessage += $": {messageProperty.GetString()}";
                        }
                    }
                    catch
                    {
                        errorMessage += $": {errorContent}";
                    }

                    return new CmpApiResponse
                    {
                        IsSuccess = false,
                        ErrorMessage = errorMessage,
                        StatusCode = response.StatusCode
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while posting to CMP API");
                return new CmpApiResponse
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private async Task<string> GetContentTypeGuidForCmpAsync(string contentTypeName, IContent cmsContent)
        {
            try
            {
                // Check cache first
                if (_contentTypeGuidCache.TryGetValue(contentTypeName, out var cachedGuid))
                {
                    _logger.LogDebug("Using cached content type GUID {Guid} for {ContentType}", cachedGuid, contentTypeName);
                    return cachedGuid;
                }

                // Generate a deterministic GUID based on content type name
                var contentTypeGuid = GenerateContentTypeGuid(contentTypeName);

                // Check if this content type exists in CMP
                var existsInCmp = await CheckContentTypeExistsInCmpAsync(contentTypeGuid);

                if (existsInCmp)
                {
                    // Content type exists, cache and return it
                    _contentTypeGuidCache[contentTypeName] = contentTypeGuid;
                    _logger.LogDebug("✅ Content type {ContentTypeGuid} exists in CMP for {ContentType}", contentTypeGuid, contentTypeName);
                    return contentTypeGuid;
                }

                // Content type doesn't exist in CMP, create it
                var newContentTypeGuid = await CreateContentTypeInCmpAsync(contentTypeName, cmsContent);

                if (!string.IsNullOrEmpty(newContentTypeGuid))
                {
                    _contentTypeGuidCache[contentTypeName] = newContentTypeGuid;
                    _logger.LogInformation("✅ Created new content type in CMP with GUID {Guid} for {ContentType}", newContentTypeGuid, contentTypeName);
                    return newContentTypeGuid;
                }

                // If creation failed, throw an exception
                throw new InvalidOperationException($"Failed to create content type '{contentTypeName}' in CMP");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting content type GUID for {ContentType}", contentTypeName);
                throw;
            }
        }

        private async Task<bool> CheckContentTypeExistsInCmpAsync(string contentTypeGuid)
        {
            try
            {
                var accessToken = await _cmpTokenService.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(accessToken))
                    return false;

                using var httpClient = _httpClientFactory.CreateClient("CmpApiClient");
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                var apiUrl = $"structured-content/content-types/{contentTypeGuid}";
                var response = await httpClient.GetAsync(apiUrl);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("✅ Content type {ContentTypeGuid} exists in CMP", contentTypeGuid);
                    return true;
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogDebug("❌ Content type {ContentTypeGuid} not found in CMP", contentTypeGuid);
                    return false;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("⚠️ Failed to check content type {ContentTypeGuid} in CMP. Status: {StatusCode}, Error: {Error}",
                        contentTypeGuid, response.StatusCode, errorContent);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception while checking content type {ContentTypeGuid} in CMP", contentTypeGuid);
                return false;
            }
        }

        private async Task<string> CreateContentTypeInCmpAsync(string contentTypeName, IContent cmsContent)
        {
            try
            {
                var accessToken = await _cmpTokenService.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(accessToken)) return null;

                using var httpClient = _httpClientFactory.CreateClient("CmpApiClient");
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                // Generate field definitions based on CMS content properties
                var fieldDefinitions = GenerateFieldDefinitionsFromCmsContent(cmsContent);

                // Create the payload according to the API documentation
                var contentTypePayload = new
                {
                    details = new
                    {
                        name = contentTypeName, // Required: Name of the content type
                        description = $"Content type created from Optimizely CMS: {contentTypeName}", // Optional but recommended
                        component = false, // Required: Indicates whether the content type is a component
                        disabled = false, // Optional: Disabled status of the content type
                        thumbnail_guid = (string)null // Optional: Thumbnail GUID of the content type
                    },
                    field_definitions = fieldDefinitions, // Required: Array of field definitions
                    created_by = "optimizely-cms-integration", // Required: User ID who is creating the content type
                    source = "Optimizely CMS", // Optional: Source for the content type
                    //expected_locales = new[] { "en", "en-US" }, // Optional: Expected locales for the content type
                    source_id = contentTypeName, // Optional: Source ID for the content type
                    source_metadata = JsonSerializer.Serialize(new // Optional: Source metadata for the content type
                    {
                        cmsContentTypeName = contentTypeName,
                        cmsContentTypeFullName = cmsContent.GetOriginalType().FullName,
                        createdAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        createdFrom = "Optimizely CMS 12"
                    })
                };

                var json = JsonSerializer.Serialize(contentTypePayload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                    WriteIndented = true // Optional: for better debugging
                });

                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                var apiUrl = "structured-content/content-types";

                _logger.LogInformation("🔄 Creating content type '{ContentTypeName}' in CMP", contentTypeName);
                _logger.LogDebug("Create content type payload: {Payload}", json);

                var response = await httpClient.PostAsync(apiUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var responseJson = JsonSerializer.Deserialize<JsonElement>(responseContent);

                    // Extract the content type GUID from the response
                    if (responseJson.TryGetProperty("content_type_guid", out var contentTypeGuidElement))
                    {
                        var contentTypeGuid = contentTypeGuidElement.GetString();
                        _logger.LogInformation("✅ Successfully created content type '{ContentTypeName}' in CMP with GUID {ContentTypeGuid}",
                            contentTypeName, contentTypeGuid);
                        return contentTypeGuid;
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Content type created but could not extract GUID from response: {Response}", responseContent);
                        return null;
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("❌ Failed to create content type '{ContentTypeName}' in CMP. Status: {StatusCode}, Error: {Error}",
                        contentTypeName, response.StatusCode, errorContent);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception while creating content type '{ContentTypeName}' in CMP", contentTypeName);
                return null;
            }
        }

        private List<object> GenerateFieldDefinitionsFromCmsContent(IContent cmsContent)
        {
            var fieldDefinitions = new List<object>();
            try
            {
                // Start with a simple text field to test
                fieldDefinitions.Add(CreateTextFieldDefinition("title", "Title", true, 1));
                fieldDefinitions.Add(CreateTextFieldDefinition("description", "Description", false, 2));

                // Add more fields based on CMS content properties
                var contentData = cmsContent as ContentData;
                if (contentData != null)
                {
                    int orderIndex = 3;
                    foreach (var property in contentData.Property)
                    {
                        if (property?.Value != null && !string.IsNullOrEmpty(property.Name) &&
                            !IsSystemProperty(property.Name))
                        {
                            var fieldDefinition = CreateFieldDefinitionFromProperty(property, orderIndex);
                            if (fieldDefinition != null)
                            {
                                fieldDefinitions.Add(fieldDefinition);
                                orderIndex++;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Warning: Could not generate all field definitions from CMS content");

                // Ensure we have at least one basic field
                if (fieldDefinitions.Count == 0)
                {
                    fieldDefinitions.Add(CreateTextFieldDefinition("title", "Title", true, 1));
                }
            }
            return fieldDefinitions;
        }

        private bool IsSystemProperty(string propertyName)
        {
            // Skip common Optimizely system properties
            var systemProperties = new[]
            {
                "PageTypeID", "PageGuid", "PageLink", "ContentLink", "ParentLink",
                "WorkID", "ContentTypeID", "ArchiveLink", "ShortcutLink", "LinkType",
                "ExternalURL", "TargetFrame", "ShortcutType", "Changed", "Created",
                "CreatedBy", "ChangedBy", "Saved", "StartPublish", "StopPublish",
                "VisibleInMenu", "PageName", "URLSegment", "StaticLinkURL",
                "Category", "Language", "MasterLanguage", "ExistingLanguages"
            };

            return systemProperties.Contains(propertyName, StringComparer.OrdinalIgnoreCase);
        }

        private object CreateFieldDefinitionFromProperty(PropertyData property, int orderIndex)
        {
            var fieldKey = SanitizeFieldKey(property.Name);
            var fieldName = property.Name;
            var cmpFieldType = GetCmpFieldTypeFromProperty(property);

            if (cmpFieldType == null) return null;

            // Base core definition
            var coreDefinition = new
            {
                key = fieldKey,
                name = fieldName,
                is_list = false,
                is_required = false,
                need_internationalization = ShouldFieldSupportInternationalization(cmpFieldType),
                field_type = cmpFieldType
            };

            // Return field definition based on type
            return cmpFieldType switch
            {
                "text-field" => new
                {
                    core = coreDefinition,
                    validation_pattern = "",
                    min_length = 0
                },

                "number" => new
                {
                    core = coreDefinition
                },

                "boolean" => new
                {
                    core = coreDefinition,
                    base_type = "boolean"
                },

                "datetime" => new
                {
                    core = coreDefinition,
                    min_date = "1900-01-01T00:00:00Z",
                    max_date = "2100-12-31T23:59:59Z"
                },

                "rich-text" => new
                {
                    core = coreDefinition,
                    min_visual_text_length = 0
                },

                _ => new
                {
                    core = coreDefinition,
                    validation_pattern = "",
                    min_length = 0
                }
            };
        }

        private string GetCmpFieldTypeFromProperty(PropertyData property)
        {
            // Keep it simple for now
            return property.Value switch
            {
                string str when str.Length > 500 || str.Contains("<") => "rich-text",
                string => "text-field",
                int or long or decimal or float or double => "number",
                bool => "boolean",
                DateTime => "datetime",
                _ => "text-field" // Default to text-field for unknown types
            };
        }

        private bool ShouldFieldSupportInternationalization(string fieldType)
        {
            return fieldType switch
            {
                "text-field" or "rich-text" => true,
                _ => false
            };
        }

        private string SanitizeFieldKey(string propertyName)
        {
            // Convert to lowercase, replace spaces and special characters with underscores
            return propertyName.ToLower()
                .Replace(" ", "_")
                .Replace("-", "_")
                .Replace(".", "_")
                .RegexReplace(@"[^a-z0-9_]", "");
        }

        private string GetPublicContentUrl(IContent cmsContent)
        {
            try
            {
                if (cmsContent is PageData page)
                {
                    var url = _urlResolver.GetUrl(page.ContentLink);
                    if (!string.IsNullOrEmpty(url) && !url.StartsWith("/"))
                    {
                        return url;
                    }
                    else if (!string.IsNullOrEmpty(url))
                    {
                        // Convert relative URL to absolute
                        var baseUrl = _config.GetValue<string>("CmpSettings:SiteBaseUrl") ?? "https://localhost:5000";
                        return $"{baseUrl.TrimEnd('/')}{url}";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not resolve public URL for content {ContentRef}", cmsContent.ContentLink);
            }

            return null;
        }

        private object CreateTextFieldDefinition(string key, string name, bool isRequired, int orderIndex)
        {
            return new
            {
                core = new
                {
                    key = key,
                    name = name,
                    is_list = false,
                    is_required = isRequired,
                    need_internationalization = true,
                    field_type = "text-field"
                },
                validation_pattern = "",
                min_length = isRequired ? 1 : 0
            };
        }

        private string GenerateContentTypeGuid(string contentTypeName)
        {
            // Generate a deterministic GUID based on content type name
            // This ensures the same content type always gets the same GUID
            using var md5 = System.Security.Cryptography.MD5.Create();
            var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"OptimizelyComsContentType_{contentTypeName}"));

            // Convert to GUID format
            var guid = new Guid(hash);
            return guid.ToString();
        }

        private object CreateFieldsStructure(IContent cmsContent, string locale)
        {
            var fieldsData = new Dictionary<string, object>();

            try
            {
                // Create field values for title (always required)
                var titleFieldValues = new[]
                {
                    new
                    {
                        order_index = 0,
                        text_value = cmsContent.Name
                    }
                };

                fieldsData["title"] = new object[]
                {
                    new
                    {
                        locale = locale,
                        field_values = titleFieldValues
                    }
                };

                // Create field values for description (if available)
                var descriptionValue = ExtractDescriptionFromContent(cmsContent);
                if (!string.IsNullOrEmpty(descriptionValue))
                {
                    var descriptionFieldValues = new[]
                    {
                        new
                        {
                            order_index = 0,
                            text_value = descriptionValue
                        }
                    };

                    fieldsData["description"] = new object[]
                    {
                        new
                        {
                            locale = locale,
                            field_values = descriptionFieldValues
                        }
                    };
                }

                // Add other content properties as fields
                var contentData = cmsContent as ContentData;
                if (contentData != null)
                {
                    int fieldIndex = 0;
                    foreach (var property in contentData.Property)
                    {
                        if (property?.Value != null && !string.IsNullOrEmpty(property.Name) &&
                            !IsSystemProperty(property.Name) &&
                            !IsSpecialProperty(property.Name)) // Skip title, description as we handle them separately
                        {
                            var fieldKey = SanitizeFieldKey(property.Name);
                            var fieldValue = CreateFieldValueFromProperty(property, 0);

                            if (fieldValue != null && !fieldsData.ContainsKey(fieldKey))
                            {
                                fieldsData[fieldKey] = new object[]
                                {
                                    new
                                    {
                                        locale = locale,
                                        field_values = new[] { fieldValue }
                                    }
                                };
                                fieldIndex++;
                            }
                        }
                    }
                }

                // Set has_embedded to false since we're not using embedded content
                //fieldsData["has_embedded"] = false;

                return fieldsData;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Warning: Could not create fields structure for content {ContentRef}", cmsContent.ContentLink);

                // Fallback to minimal structure
                return new Dictionary<string, object>
                {
                    ["title"] = new object[]
                    {
                        new
                        {
                            locale = locale,
                            field_values = new[]
                            {
                                new
                                {
                                    order_index = 0,
                                    text_value = cmsContent.Name
                                }
                            }
                        }
                    }
                    //["has_embedded"] = false
                };
            }
        }

        private string ExtractDescriptionFromContent(IContent cmsContent)
        {
            var contentData = cmsContent as ContentData;
            if (contentData != null)
            {
                foreach (var property in contentData.Property)
                {
                    if (property?.Value != null && !string.IsNullOrEmpty(property.Name))
                    {
                        switch (property.Name.ToLower())
                        {
                            case "mainbody":
                            case "description":
                            case "content":
                            case "text":
                            case "summary":
                                return property.Value.ToString();
                        }
                    }
                }
            }
            return null;
        }

        private bool IsSpecialProperty(string propertyName)
        {
            var specialProperties = new[] { "title", "name", "heading", "mainbody", "description", "content", "text" };
            return specialProperties.Contains(propertyName.ToLower());
        }

        private object CreateFieldValueFromProperty(PropertyData property, int orderIndex)
        {
            return property.Value switch
            {
                string str when str.Length > 500 || str.Contains("<") => new
                {
                    order_index = orderIndex,
                    rich_text_value = str
                },
                string str => new
                {
                    order_index = orderIndex,
                    text_value = str
                },
                int intVal => new
                {
                    order_index = orderIndex,
                    num_value = (double)intVal
                },
                long longVal => new
                {
                    order_index = orderIndex,
                    num_value = (double)longVal
                },
                decimal decimalVal => new
                {
                    order_index = orderIndex,
                    num_value = (double)decimalVal
                },
                float floatVal => new
                {
                    order_index = orderIndex,
                    num_value = (double)floatVal
                },
                double doubleVal => new
                {
                    order_index = orderIndex,
                    num_value = doubleVal
                },
                bool boolVal => new
                {
                    order_index = orderIndex,
                    bool_value = boolVal
                },
                DateTime dateVal => new
                {
                    order_index = orderIndex,
                    datetime_value = dateVal.ToString("yyyy-MM-ddTHH:mm:ssZ")
                },
                ContentReference contentRef when !ContentReference.IsNullOrEmpty(contentRef) => new
                {
                    order_index = orderIndex,
                    text_value = $"ContentReference: {contentRef}"
                },
                _ => new
                {
                    order_index = orderIndex,
                    text_value = property.Value?.ToString() ?? ""
                }
            };
        }
    }

    // Helper classes for responses and exceptions
    public class CmpApiResponse
    {
        public bool IsSuccess { get; set; }
        public string ContentId { get; set; }
        public string ErrorMessage { get; set; }
        public HttpStatusCode StatusCode { get; set; }
        public string ResponseContent { get; set; }
    }

    public class CmpApiException : Exception
    {
        public CmpApiException(string message) : base(message) { }
        public CmpApiException(string message, Exception innerException) : base(message, innerException) { }
    }
}
