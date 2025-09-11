using Azure.Core;
using EPiServer.Core;
using EPiServer.Web.Routing;
using System.Net.Http.Headers;
using System.Text.Json;

namespace alloy_events_test.CMPIntegration
{
    public interface ICmpIntegrationService
    {
        Task<CmpAttachmentResult> AttachContentToTaskAsync(string cmpTaskId, ContentReference contentRef, string contentName, string contentUrl);
        Task<CmpAttachmentResult> MarkTaskAsCompletedAsync(string cmpTaskId, string completionComment = null);
    }

    public class CmpAttachmentResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string ErrorDetails { get; set; }
        public int? StatusCode { get; set; }
        public int CompletedStepsCount { get; set; }
        public int TotalStepsCount { get; set; }
    }

    public class CmpIntegrationService : ICmpIntegrationService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<CmpIntegrationService> _logger;
        private readonly IConfiguration _config;
        private readonly IContentRepository _contentRepository;
        private readonly IUrlResolver _urlResolver;
        private readonly ICmpTaskCompletionService _taskCompletionService;
        private static readonly Dictionary<string, string> _contentTypeGuidCache = new();

        public CmpIntegrationService(
            HttpClient httpClient, 
            ILogger<CmpIntegrationService> logger, 
            IConfiguration config,
            IContentRepository contentRepository,
            IUrlResolver urlResolver,
            ICmpTaskCompletionService taskCompletionService)
        {
            _httpClient = httpClient;
            _logger = logger;
            _config = config;
            _contentRepository = contentRepository;
            _urlResolver = urlResolver;
            _taskCompletionService = taskCompletionService;
        }

        public async Task<CmpAttachmentResult> AttachContentToTaskAsync(string cmpTaskId, ContentReference contentRef, string contentName, string contentUrl)
        {
            try
            {
                var accessToken = await GetAccessTokenAsync();

                if (string.IsNullOrEmpty(accessToken))
                {
                    _logger.LogWarning("⚠️ No CMP access token available. Cannot attach content to task {TaskId}", cmpTaskId);
                    return new CmpAttachmentResult
                    {
                        Success = false,
                        Message = "No CMP access token available",
                        ErrorDetails = "OAuth authentication required"
                    };
                }

                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                // Get the published content from CMS
                var cmsContent = _contentRepository.Get<IContent>(contentRef);
                
                if (cmsContent == null)
                {
                    _logger.LogError("❌ Cannot find CMS content with reference {ContentRef}", contentRef);
                    return new CmpAttachmentResult
                    {
                        Success = false,
                        Message = $"Cannot find CMS content with reference {contentRef}",
                        ErrorDetails = "Content not found in CMS"
                    };
                }

                // Use the actual CMP API endpoint for structured contents
                var apiUrl = $"https://api.cmp.optimizely.com/v3/tasks/{cmpTaskId}/structured-contents";

                // Create structured content from published CMS content
                var structuredContentPayload = await CreateStructuredContentFromCmsContentAsync(cmsContent, contentRef, contentUrl);

                var json = JsonSerializer.Serialize(structuredContentPayload, new JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });
                
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                _logger.LogInformation("🔄 Attaching CMS content '{ContentName}' as structured content to CMP task {TaskId}", 
                    cmsContent.Name, cmpTaskId);
                _logger.LogDebug("API URL: {ApiUrl}", apiUrl);
                _logger.LogDebug("Payload: {Payload}", json);

                var response = await _httpClient.PostAsync(apiUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    _logger.LogInformation("✅ Successfully attached CMS content '{ContentName}' as structured content to CMP task {TaskId}", 
                        cmsContent.Name, cmpTaskId);
                    _logger.LogDebug("Response: {Response}", responseContent);
                    return new CmpAttachmentResult
                    {
                        Success = true,
                        Message = $"Successfully attached content '{cmsContent.Name}' to CMP task {cmpTaskId}",
                        StatusCode = (int)response.StatusCode
                    };
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("❌ Failed to attach CMS structured content to CMP task {TaskId}. Status: {StatusCode}, Error: {Error}", 
                        cmpTaskId, response.StatusCode, errorContent);
                    return new CmpAttachmentResult
                    {
                        Success = false,
                        Message = $"Failed to attach content to CMP task {cmpTaskId}",
                        ErrorDetails = errorContent,
                        StatusCode = (int)response.StatusCode
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Exception while attaching CMS structured content to CMP task {TaskId}", cmpTaskId);
                return new CmpAttachmentResult
                {
                    Success = false,
                    Message = $"Exception occurred while attaching content to CMP task {cmpTaskId}",
                    ErrorDetails = ex.Message
                };
            }
        }

        public async Task<CmpAttachmentResult> MarkTaskAsCompletedAsync(string cmpTaskId, string completionComment = null)
        {
            try
            {
                _logger.LogInformation("🔄 Delegating task completion to CmpTaskCompletionService for task {TaskId}", cmpTaskId);

                var result = await _taskCompletionService.CompleteTaskAsync(cmpTaskId, completionComment);

                return new CmpAttachmentResult
                {
                    Success = result.Success,
                    Message = result.Message,
                    ErrorDetails = result.ErrorDetails,
                    StatusCode = result.StatusCode,
                    CompletedStepsCount = result.CompletedStepsCount,
                    TotalStepsCount = result.TotalStepsCount
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Exception while completing CMP task {TaskId}", cmpTaskId);
                return new CmpAttachmentResult
                {
                    Success = false,
                    Message = $"Exception occurred while completing task {cmpTaskId}",
                    ErrorDetails = ex.Message
                };
            }
        }

        private async Task<object> CreateStructuredContentFromCmsContentAsync(IContent cmsContent, ContentReference contentRef, string contentUrl)
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

            var structuredContentPayload = new
            {
                content_type_guid = contentTypeGuid, // Required
                template_guid = (string)null, // Optional
                fields = fieldsData, // Correctly structured fields according to StructuredContentBody schema
                root_content = true, // Required - indicates this is not embedded content
                title = cmsContent.Name, // Title of the structured content
                primary_locale = localizable?.Language?.Name ?? "en", // Primary locale
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
                })
            };

            return structuredContentPayload;
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
                fieldsData["has_embedded"] = false;

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
                    },
                    ["has_embedded"] = false
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

        private Dictionary<string, object> ExtractContentProperties(IContent cmsContent)
        {
            var properties = new Dictionary<string, object>();

            try
            {
                var contentData = cmsContent as ContentData;
                if (contentData != null)
                {
                    foreach (var property in contentData.Property)
                    {
                        if (property?.Value != null && !string.IsNullOrEmpty(property.Name))
                        {
                            // Extract common properties that might be useful in CMP
                            switch (property.Name.ToLower())
                            {
                                case "heading":
                                case "title":
                                case "name":
                                    properties["title"] = property.Value.ToString();
                                    break;
                                case "mainbody":
                                case "description":
                                case "content":
                                case "text":
                                    properties["description"] = property.Value.ToString();
                                    break;
                                case "metadescription":
                                    properties["meta_description"] = property.Value.ToString();
                                    break;
                                case "keywords":
                                case "tags":
                                    properties["keywords"] = property.Value.ToString();
                                    break;
                                case "image":
                                case "teaserimage":
                                    if (property.Value is ContentReference imageRef && !ContentReference.IsNullOrEmpty(imageRef))
                                    {
                                        properties["image_reference"] = imageRef.ToString();
                                    }
                                    break;
                                default:
                                    // Include other properties as generic fields
                                    if (property.Value is string || property.Value is int || property.Value is bool || property.Value is DateTime)
                                    {
                                        properties[property.Name.ToLower()] = property.Value;
                                    }
                                    break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Warning: Could not extract all properties from CMS content {ContentRef}", cmsContent.ContentLink);
            }

            return properties;
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

        private async Task<bool> CheckContentTypeExistsInCmpAsync(string contentTypeGuid)
        {
            try
            {
                var accessToken = await GetAccessTokenAsync();
                if (string.IsNullOrEmpty(accessToken))
                    return false;

                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                var apiUrl = $"https://api.cmp.optimizely.com/v3/structured-content/content-types/{contentTypeGuid}";
                var response = await _httpClient.GetAsync(apiUrl);

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
                var accessToken = await GetAccessTokenAsync();
                if (string.IsNullOrEmpty(accessToken)) return null;

                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

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
                var apiUrl = "https://api.cmp.optimizely.com/v3/structured-content/content-types";

                _logger.LogInformation("🔄 Creating content type '{ContentTypeName}' in CMP", contentTypeName);
                _logger.LogDebug("Create content type payload: {Payload}", json);

                var response = await _httpClient.PostAsync(apiUrl, content);

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

        private bool IsJsonProperty(PropertyData property)
        {
            // Add logic to detect if a property should be treated as JSON
            // This could be based on property type, naming convention, or content analysis
            var propertyName = property.Name.ToLower();
            return propertyName.Contains("json") ||
                   propertyName.Contains("metadata") ||
                   propertyName.Contains("config");
        }

        private async Task<string> GetAccessTokenAsync()
        {
            // TODO: Implement OAuth token retrieval
            return _config.GetValue<string>("CmpSettings:AccessToken");
        }
    }
}