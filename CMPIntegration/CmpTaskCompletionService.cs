using System.Net.Http.Headers;
using System.Text.Json;

namespace alloy_events_test.CMPIntegration
{
    public interface ICmpTaskCompletionService
    {
        Task<CmpTaskCompletionResult> CompleteTaskAsync(string cmpTaskId, string completionComment = null);
        Task<CmpTaskCompletionResult> GetTaskDetailsAsync(string cmpTaskId);
        Task<CmpTaskCompletionResult> CompleteAllTaskStepsAsync(string cmpTaskId, string completionComment = null);
    }

    public class CmpTaskCompletionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string ErrorDetails { get; set; }
        public int? StatusCode { get; set; }
        public object TaskData { get; set; }
        public int CompletedStepsCount { get; set; }
        public int TotalStepsCount { get; set; }
        public List<string> CompletedSubSteps { get; set; } = new List<string>();
        public List<string> FailedSubSteps { get; set; } = new List<string>();
        public List<string> SkippedSubSteps { get; set; } = new List<string>();
    }

    public class CmpTaskCompletionService : ICmpTaskCompletionService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<CmpTaskCompletionService> _logger;
        private readonly IConfiguration _config;

        public CmpTaskCompletionService(
            HttpClient httpClient,
            ILogger<CmpTaskCompletionService> logger,
            IConfiguration config)
        {
            _httpClient = httpClient;
            _logger = logger;
            _config = config;
        }

        public async Task<CmpTaskCompletionResult> CompleteTaskAsync(string cmpTaskId, string completionComment = null)
        {
            try
            {
                _logger.LogInformation("🔄 Starting complete task process for CMP task {TaskId}", cmpTaskId);

                // First, get task details to understand its structure
                var taskDetailsResult = await GetTaskDetailsAsync(cmpTaskId);
                if (!taskDetailsResult.Success)
                {
                    return taskDetailsResult;
                }

                // Complete all sub-steps to complete the entire task
                var stepsResult = await CompleteAllTaskStepsAsync(cmpTaskId, completionComment);
                
                _logger.LogInformation("✅ Task completion process finished for CMP task {TaskId}. " +
                                     "Completed: {CompletedSteps}/{TotalSteps}, Failed: {FailedSteps}, Skipped: {SkippedSteps}", 
                    cmpTaskId, stepsResult.CompletedStepsCount, stepsResult.TotalStepsCount, 
                    stepsResult.FailedSubSteps.Count, stepsResult.SkippedSubSteps.Count);
                    
                return stepsResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Exception while completing CMP task {TaskId}", cmpTaskId);
                return new CmpTaskCompletionResult
                {
                    Success = false,
                    Message = $"Exception occurred while completing task {cmpTaskId}",
                    ErrorDetails = ex.Message
                };
            }
        }

        public async Task<CmpTaskCompletionResult> CompleteAllTaskStepsAsync(string cmpTaskId, string completionComment = null)
        {
            try
            {
                var accessToken = await GetAccessTokenAsync();
                if (string.IsNullOrEmpty(accessToken))
                {
                    return new CmpTaskCompletionResult
                    {
                        Success = false,
                        Message = "No CMP access token available",
                        ErrorDetails = "OAuth authentication required"
                    };
                }

                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                // Get task details to find all steps and sub-steps
                var taskDetailsResult = await GetTaskDetailsAsync(cmpTaskId);
                if (!taskDetailsResult.Success)
                {
                    return taskDetailsResult;
                }

                var taskData = taskDetailsResult.TaskData as JsonElement?;
                if (!taskData.HasValue)
                {
                    _logger.LogWarning("⚠️ No task data available for task {TaskId}", cmpTaskId);
                    return new CmpTaskCompletionResult
                    {
                        Success = false,
                        Message = "Unable to retrieve task structure",
                        ErrorDetails = "Task data is not available"
                    };
                }

                var result = new CmpTaskCompletionResult
                {
                    Success = true,
                    Message = "Task completion process started"
                };

                var totalSubStepsCount = 0;
                var completedSubStepsCount = 0;

                // Extract and complete all sub-steps
                if (taskData.Value.TryGetProperty("steps", out var stepsElement) && 
                    stepsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var step in stepsElement.EnumerateArray())
                    {
                        if (step.TryGetProperty("id", out var stepIdElement))
                        {
                            var stepId = stepIdElement.GetString();
                            _logger.LogDebug("🔍 Processing step {StepId}", stepId);
                            
                            // Process sub-steps within this step
                            if (step.TryGetProperty("sub_steps", out var subStepsElement) && 
                                subStepsElement.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var subStep in subStepsElement.EnumerateArray())
                                {
                                    totalSubStepsCount++;
                                    
                                    if (subStep.TryGetProperty("id", out var subStepIdElement))
                                    {
                                        var subStepId = subStepIdElement.GetString();
                                        
                                        // Check current status of sub-step
                                        var isAlreadyCompleted = IsSubStepCompleted(subStep);
                                        var isSkipped = IsSubStepSkipped(subStep);
                                        
                                        if (isAlreadyCompleted)
                                        {
                                            _logger.LogDebug("✅ Sub-step {SubStepId} in step {StepId} is already completed", subStepId, stepId);
                                            completedSubStepsCount++;
                                            result.CompletedSubSteps.Add($"{stepId}/{subStepId}");
                                        }
                                        else if (isSkipped)
                                        {
                                            _logger.LogDebug("⏭️ Sub-step {SubStepId} in step {StepId} is skipped", subStepId, stepId);
                                            result.SkippedSubSteps.Add($"{stepId}/{subStepId}");
                                        }
                                        else
                                        {
                                            // Complete this sub-step
                                            var subStepResult = await CompleteSubStepAsync(cmpTaskId, stepId, subStepId, completionComment);
                                            if (subStepResult.Success)
                                            {
                                                completedSubStepsCount++;
                                                result.CompletedSubSteps.Add($"{stepId}/{subStepId}");
                                                _logger.LogDebug("✅ Completed sub-step {SubStepId} in step {StepId}", subStepId, stepId);
                                            }
                                            else
                                            {
                                                result.FailedSubSteps.Add($"{stepId}/{subStepId}: {subStepResult.Message}");
                                                _logger.LogWarning("⚠️ Failed to complete sub-step {SubStepId} in step {StepId}: {Error}", 
                                                    subStepId, stepId, subStepResult.Message);
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                _logger.LogDebug("ℹ️ Step {StepId} has no sub-steps", stepId);
                            }
                        }
                    }
                }

                result.TotalStepsCount = totalSubStepsCount;
                result.CompletedStepsCount = completedSubStepsCount;

                // Determine overall success
                var hasFailures = result.FailedSubSteps.Count > 0;
                var allProcessed = (completedSubStepsCount + result.SkippedSubSteps.Count + result.FailedSubSteps.Count) == totalSubStepsCount;

                result.Success = !hasFailures && allProcessed;
                result.Message = hasFailures ? 
                    $"Task completion finished with {result.FailedSubSteps.Count} failures out of {totalSubStepsCount} sub-steps" :
                    $"Successfully processed all {totalSubStepsCount} sub-steps ({completedSubStepsCount} completed, {result.SkippedSubSteps.Count} skipped)";

                _logger.LogInformation("🔄 Task {TaskId} processing summary: {CompletedSteps} completed, {SkippedSteps} skipped, {FailedSteps} failed out of {TotalSteps} total sub-steps", 
                    cmpTaskId, completedSubStepsCount, result.SkippedSubSteps.Count, result.FailedSubSteps.Count, totalSubStepsCount);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Exception while completing all steps for CMP task {TaskId}", cmpTaskId);
                return new CmpTaskCompletionResult
                {
                    Success = false,
                    Message = $"Exception occurred while completing steps for task {cmpTaskId}",
                    ErrorDetails = ex.Message
                };
            }
        }

        public async Task<CmpTaskCompletionResult> GetTaskDetailsAsync(string cmpTaskId)
        {
            try
            {
                var accessToken = await GetAccessTokenAsync();
                if (string.IsNullOrEmpty(accessToken))
                {
                    return new CmpTaskCompletionResult
                    {
                        Success = false,
                        Message = "No CMP access token available",
                        ErrorDetails = "OAuth authentication required"
                    };
                }

                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                var apiUrl = $"https://api.cmp.optimizely.com/v3/tasks/{cmpTaskId}";
                _logger.LogDebug("🔍 Getting task details from: {ApiUrl}", apiUrl);

                var response = await _httpClient.GetAsync(apiUrl);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var taskData = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    
                    _logger.LogDebug("✅ Successfully retrieved task details for {TaskId}", cmpTaskId);
                    
                    return new CmpTaskCompletionResult
                    {
                        Success = true,
                        Message = "Task details retrieved successfully",
                        StatusCode = (int)response.StatusCode,
                        TaskData = taskData
                    };
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    var statusCode = (int)response.StatusCode;
                    
                    var errorMessage = statusCode switch
                    {
                        404 => $"CMP task {cmpTaskId} not found",
                        401 => "Unauthorized - invalid or expired access token",
                        403 => "Forbidden - insufficient permissions to access this task",
                        _ => $"Failed to retrieve CMP task {cmpTaskId} details"
                    };
                    
                    _logger.LogError("❌ Failed to get task details for {TaskId}. Status: {StatusCode}, Error: {Error}", 
                        cmpTaskId, response.StatusCode, errorContent);
                        
                    return new CmpTaskCompletionResult
                    {
                        Success = false,
                        Message = errorMessage,
                        ErrorDetails = errorContent,
                        StatusCode = statusCode
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Exception while getting task details for {TaskId}", cmpTaskId);
                return new CmpTaskCompletionResult
                {
                    Success = false,
                    Message = $"Exception occurred while getting task {cmpTaskId} details",
                    ErrorDetails = ex.Message
                };
            }
        }

        private async Task<CmpTaskCompletionResult> CompleteSubStepAsync(string cmpTaskId, string stepId, string subStepId, string completionComment)
        {
            try
            {
                var apiUrl = $"https://api.cmp.optimizely.com/v3/tasks/{cmpTaskId}/steps/{stepId}/sub-steps/{subStepId}";

                // Use the correct API parameters as per documentation
                var updatePayload = new
                {
                    is_completed = true // This is the correct parameter according to the API documentation
                };

                var json = JsonSerializer.Serialize(updatePayload, new JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });
                
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                
                _logger.LogDebug("🔄 Completing sub-step {SubStepId} in step {StepId} for task {TaskId}", subStepId, stepId, cmpTaskId);
                _logger.LogTrace("API URL: {ApiUrl}, Payload: {Payload}", apiUrl, json);

                var response = await _httpClient.PatchAsync(apiUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    _logger.LogTrace("API Response: {Response}", responseContent);
                    
                    return new CmpTaskCompletionResult
                    {
                        Success = true,
                        Message = $"Sub-step {subStepId} completed successfully",
                        StatusCode = (int)response.StatusCode
                    };
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    var statusCode = (int)response.StatusCode;
                    
                    var errorMessage = statusCode switch
                    {
                        404 => $"Sub-step {subStepId} not found in step {stepId} for task {cmpTaskId}",
                        401 => "Unauthorized - invalid or expired access token",
                        403 => "Forbidden - insufficient permissions to complete this sub-step",
                        409 => $"Conflict - sub-step {subStepId} may already be completed or in an invalid state",
                        400 => $"Bad request - invalid parameters for sub-step {subStepId}",
                        _ => $"Failed to complete sub-step {subStepId}"
                    };
                    
                    _logger.LogError("❌ Failed to complete sub-step {SubStepId} in step {StepId}. Status: {StatusCode}, Error: {Error}", 
                        subStepId, stepId, response.StatusCode, errorContent);
                    
                    return new CmpTaskCompletionResult
                    {
                        Success = false,
                        Message = errorMessage,
                        ErrorDetails = errorContent,
                        StatusCode = statusCode
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Exception while completing sub-step {SubStepId} in step {StepId}", subStepId, stepId);
                return new CmpTaskCompletionResult
                {
                    Success = false,
                    Message = $"Exception while completing sub-step {subStepId}",
                    ErrorDetails = ex.Message
                };
            }
        }

        private bool IsSubStepCompleted(JsonElement subStep)
        {
            if (subStep.TryGetProperty("is_completed", out var completedElement))
            {
                return completedElement.ValueKind == JsonValueKind.True;
            }
            
            // Also check for status field if available
            if (subStep.TryGetProperty("status", out var statusElement))
            {
                return statusElement.GetString()?.ToLower() == "completed";
            }
            
            return false;
        }

        private bool IsSubStepSkipped(JsonElement subStep)
        {
            if (subStep.TryGetProperty("is_skipped", out var skippedElement))
            {
                return skippedElement.ValueKind == JsonValueKind.True;
            }
            
            return false;
        }

        private async Task<string> GetAccessTokenAsync()
        {
            // TODO: Implement OAuth token retrieval or get from configuration
            return _config.GetValue<string>("CmpSettings:AccessToken");
        }
    }
}