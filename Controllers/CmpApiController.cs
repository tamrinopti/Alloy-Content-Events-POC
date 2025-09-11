using Microsoft.AspNetCore.Mvc;
using alloy_events_test.CMPIntegration;
using EPiServer.ServiceLocation;

namespace alloy_events_test.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CmpApiController : ControllerBase
    {
        private readonly ICmpIntegrationService _cmpService;
        private readonly ICmpTaskCompletionService _taskCompletionService;
        private readonly ILogger<CmpApiController> _logger;

        public CmpApiController(
            ICmpIntegrationService cmpService, 
            ICmpTaskCompletionService taskCompletionService,
            ILogger<CmpApiController> logger)
        {
            _cmpService = cmpService;
            _taskCompletionService = taskCompletionService;
            _logger = logger;
        }

        [HttpPost("attach-published-content")]
        public async Task<IActionResult> AttachPublishedContentToTask([FromBody] AttachPublishedContentRequest request)
        {
            try
            {
                var contentRepository = ServiceLocator.Current.GetInstance<IContentRepository>();
                var content = contentRepository.Get<IContent>(ContentReference.Parse(request.ContentReference));

                if (content == null)
                {
                    return NotFound(new { success = false, message = $"Content with reference {request.ContentReference} not found" });
                }

                // Check if content is published
                if (content is IVersionable versionable &&
                    (versionable.Status != VersionStatus.Published ||
                     versionable.StartPublish > DateTime.Now ||
                     (versionable.StopPublish.HasValue && versionable.StopPublish < DateTime.Now)))
                {
                    return BadRequest(new { success = false, message = $"Content '{content.Name}' is not currently published" });
                }

                var contentUrl = GetContentEditUrl(content);

                // Call the CMP service and get the result
                var result = await _cmpService.AttachContentToTaskAsync(
                    request.CmpTaskId,
                    ContentReference.Parse(request.ContentReference),
                    content.Name,
                    contentUrl);

                // Return appropriate HTTP status based on CMP API result
                if (result.Success)
                {
                    return Ok(new
                    {
                        success = true,
                        message = result.Message,
                        contentReference = request.ContentReference,
                        contentName = content.Name,
                        contentType = content.GetOriginalType().Name,
                        publishStatus = content is IVersionable v ? v.Status.ToString() : "Unknown",
                        apiEndpoint = $"https://api.cmp.optimizely.com/v3/tasks/{request.CmpTaskId}/structured-contents",
                        statusCode = result.StatusCode
                    });
                }
                else
                {
                    // Return BadRequest if CMP API call failed
                    var statusCode = result.StatusCode ?? 500;
                    
                    if (statusCode == 404)
                    {
                        return NotFound(new
                        {
                            success = false,
                            message = result.Message,
                            errorDetails = result.ErrorDetails,
                            contentReference = request.ContentReference,
                            cmpTaskId = request.CmpTaskId,
                            statusCode = statusCode
                        });
                    }
                    else if (statusCode == 401 || statusCode == 403)
                    {
                        return Unauthorized(new
                        {
                            success = false,
                            message = result.Message,
                            errorDetails = result.ErrorDetails,
                            contentReference = request.ContentReference,
                            cmpTaskId = request.CmpTaskId,
                            statusCode = statusCode
                        });
                    }
                    else
                    {
                        return StatusCode(statusCode, new
                        {
                            success = false,
                            message = result.Message,
                            errorDetails = result.ErrorDetails,
                            contentReference = request.ContentReference,
                            cmpTaskId = request.CmpTaskId,
                            statusCode = statusCode
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to attach published content {ContentRef} to CMP task {TaskId}",
                    request.ContentReference, request.CmpTaskId);
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message,
                    contentReference = request.ContentReference,
                    cmpTaskId = request.CmpTaskId
                });
            }
        }

        [HttpPatch("complete-task")]
        public async Task<IActionResult> CompleteTask([FromBody] CompleteTaskRequest request)
        {
            try
            {
                _logger.LogInformation("🔄 Attempting to complete CMP task {TaskId} by completing all sub-steps", request.CmpTaskId);

                // Call the task completion service directly - it will complete all sub-steps
                // which automatically completes the task (no separate task completion API call needed)
                var result = await _taskCompletionService.CompleteTaskAsync(request.CmpTaskId, request.CompletionComment);

                if (result.Success)
                {
                    _logger.LogInformation("✅ Successfully completed CMP task {TaskId}", request.CmpTaskId);
                    return Ok(new
                    {
                        success = true,
                        message = result.Message,
                        cmpTaskId = request.CmpTaskId,
                        completionComment = request.CompletionComment,
                        completedAt = DateTime.UtcNow,
                        completedStepsCount = result.CompletedStepsCount,
                        totalStepsCount = result.TotalStepsCount,
                        completedSubSteps = result.CompletedSubSteps,
                        failedSubSteps = result.FailedSubSteps,
                        skippedSubSteps = result.SkippedSubSteps,
                        stepsCompletionRate = result.TotalStepsCount > 0 ? $"{result.CompletedStepsCount}/{result.TotalStepsCount}" : "N/A",
                        apiEndpoint = $"https://api.cmp.optimizely.com/v3/tasks/{request.CmpTaskId}/steps/*/sub-steps/*",
                        statusCode = result.StatusCode
                    });
                }
                else
                {
                    var statusCode = result.StatusCode ?? 500;
                    
                    _logger.LogError("❌ Failed to complete CMP task {TaskId}. Status: {StatusCode}, Message: {Message}", 
                        request.CmpTaskId, statusCode, result.Message);

                    if (statusCode == 404)
                    {
                        return NotFound(new
                        {
                            success = false,
                            message = result.Message,
                            errorDetails = result.ErrorDetails,
                            cmpTaskId = request.CmpTaskId,
                            statusCode = statusCode
                        });
                    }
                    else if (statusCode == 401 || statusCode == 403)
                    {
                        return Unauthorized(new
                        {
                            success = false,
                            message = result.Message,
                            errorDetails = result.ErrorDetails,
                            cmpTaskId = request.CmpTaskId,
                            statusCode = statusCode
                        });
                    }
                    else if (statusCode == 409)
                    {
                        return Conflict(new
                        {
                            success = false,
                            message = result.Message,
                            errorDetails = result.ErrorDetails,
                            cmpTaskId = request.CmpTaskId,
                            statusCode = statusCode
                        });
                    }
                    else
                    {
                        return StatusCode(statusCode, new
                        {
                            success = false,
                            message = result.Message,
                            errorDetails = result.ErrorDetails,
                            cmpTaskId = request.CmpTaskId,
                            statusCode = statusCode
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Exception while completing CMP task {TaskId}", request.CmpTaskId);
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message,
                    cmpTaskId = request.CmpTaskId,
                    errorType = ex.GetType().Name
                });
            }
        }

        private string GetContentEditUrl(IContent content)
        {
            return $"https://localhost:5000/episerver/cms/admin#context=epi.cms.contentdata:///{content.ContentLink}";
        }
    }

    public class AttachPublishedContentRequest
    {
        public string ContentReference { get; set; } // e.g., "5"
        public string CmpTaskId { get; set; } // The CMP task ID
    }

    public class CompleteTaskRequest
    {
        public string CmpTaskId { get; set; } // The CMP task ID to complete
        public string CompletionComment { get; set; } // Optional comment when completing the task
    }
}