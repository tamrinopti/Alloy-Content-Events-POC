using EPiServer;
using EPiServer.Core;

namespace alloy_events_test.CMPIntegration
{
    //public class CmpContentEventHandler
    //{
    //    private readonly ICmpIntegrationService _cmpService;
    //    private readonly ILogger<CmpContentEventHandler> _logger;
    //    private readonly IConfiguration _config;

    //    public CmpContentEventHandler(
    //        ICmpIntegrationService cmpService, 
    //        ILogger<CmpContentEventHandler> logger,
    //        IConfiguration config)
    //    {
    //        _cmpService = cmpService;
    //        _logger = logger;
    //        _config = config;
    //    }

    //    public async Task HandleContentPublished(object sender, ContentEventArgs e)
    //    {
    //        try
    //        {
    //            if (!_config.GetValue("CmpSettings:AutoCreateTasks", false))
    //                return;

    //            // Create review task when content is published
    //            var reviewerEmail = _config.GetValue<string>("CmpSettings:DefaultReviewerEmail");
    //            if (!string.IsNullOrEmpty(reviewerEmail))
    //            {
    //                await _cmpService.CreateTaskFromContentAsync(
    //                    e.Content, 
    //                    $"Review published content: {e.Content.Name}",
    //                    reviewerEmail
    //                );
    //            }
    //        }
    //        catch (Exception ex)
    //        {
    //            _logger.LogError(ex, "Failed to handle CMP integration for published content {ContentId}", 
    //                e.ContentLink?.ID);
    //        }
    //    }

    //    public async Task AttachToExistingTask(ContentReference contentRef, int taskId)
    //    {
    //        try
    //        {
    //            var contentUrl = GetContentEditUrl(contentRef);
    //            await _cmpService.AttachContentToTaskAsync(taskId, contentRef, contentUrl);
    //        }
    //        catch (Exception ex)
    //        {
    //            _logger.LogError(ex, "Failed to attach content {ContentRef} to task {TaskId}", 
    //                contentRef, taskId);
    //            throw;
    //        }
    //    }

    //    private string GetContentEditUrl(ContentReference contentRef)
    //    {
    //        return $"/episerver/cms/admin#context=epi.cms.contentdata:///{contentRef}";
    //    }
    //}
}