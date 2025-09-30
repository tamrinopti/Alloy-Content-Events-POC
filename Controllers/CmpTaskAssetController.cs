using Microsoft.AspNetCore.Mvc;
using alloy_events_test.CMPIntegration;

namespace alloy_events_test.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CmpTaskAssetController : ControllerBase
    {
        private readonly ICmpTaskAssetService _cmpTaskAssetService;
        private readonly ILogger<CmpTaskAssetController> _logger;

        public CmpTaskAssetController(
            ICmpTaskAssetService cmpTaskAssetService,
            ILogger<CmpTaskAssetController> logger)
        {
            _cmpTaskAssetService = cmpTaskAssetService;
            _logger = logger;
        }

        /// <summary>
        /// Attach structured content to a CMP task
        /// </summary>
        /// <param name="taskId">The ID of the task to attach content to</param>
        /// <param name="request">The attach request containing content ID</param>
        /// <returns>Result of the attach operation</returns>
        [HttpPost("{taskId}/attach-structured-content")]
        public async Task<IActionResult> AttachStructuredContentToTask(
            string taskId, 
            [FromBody] AttachStructuredContentRequest request)
        {
            try
            {
                _logger.LogInformation("🔗 Received request to attach structured content {ContentId} to task {TaskId}", 
                    request?.ContentId, taskId);

                // Validate input
                if (string.IsNullOrWhiteSpace(taskId))
                {
                    return BadRequest(new { error = "Task ID is required" });
                }

                if (request == null || string.IsNullOrWhiteSpace(request.ContentId))
                {
                    return BadRequest(new { error = "Content ID is required" });
                }

                // Call the service to attach the structured content
                var result = await _cmpTaskAssetService.AttachStructuredContentToTaskAsync(taskId, request.ContentId);

                if (result.Success)
                {
                    _logger.LogInformation("✅ Successfully attached structured content {ContentId} to task {TaskId}", 
                        request.ContentId, taskId);

                    return Ok(new
                    {
                        success = true,
                        message = "Structured content attached successfully",
                        data = new
                        {
                            assetId = result.AssetId,
                            title = result.Title,
                            type = result.Type,
                            createdAt = result.CreatedAt,
                            taskAsset = result.TaskAsset
                        }
                    });
                }
                else
                {
                    _logger.LogError("❌ Failed to attach structured content {ContentId} to task {TaskId}: {Error}", 
                        request.ContentId, taskId, result.ErrorMessage);

                    return StatusCode(500, new
                    {
                        success = false,
                        error = result.ErrorMessage,
                        details = result.ErrorDetails
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Exception in AttachStructuredContentToTask for task {TaskId}", taskId);

                return StatusCode(500, new
                {
                    success = false,
                    error = "An unexpected error occurred",
                    details = ex.Message
                });
            }
        }

        /// <summary>
        /// Attach structured content to a task using query parameters (alternative endpoint)
        /// </summary>
        /// <param name="taskId">The ID of the task</param>
        /// <param name="contentId">The ID of the structured content</param>
        /// <returns>Result of the attach operation</returns>
        [HttpPost("{taskId}/attach-content/{contentId}")]
        public async Task<IActionResult> AttachStructuredContentToTaskByPath(string taskId, string contentId)
        {
            var request = new AttachStructuredContentRequest { ContentId = contentId };
            return await AttachStructuredContentToTask(taskId, request);
        }
    }

    // Request model for the controller
    public class AttachStructuredContentRequest
    {
        /// <summary>
        /// The ID of the structured content to attach
        /// </summary>
        public string ContentId { get; set; }
    }
}