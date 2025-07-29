using alloy_events_test.Models.Pages;
using EPiServer.Security;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;


namespace alloy12.Controllers;


[ApiController]
[Route("api/[controller]")]
public class StartPageAPIController : ControllerBase
{
    private readonly IContentRepository _contentRepository;
    private readonly IContentVersionRepository _contentVersionRepository;

    public StartPageAPIController(
        IContentRepository contentRepository,
        IContentVersionRepository contentVersionRepository)
    {
        _contentRepository = contentRepository;
        _contentVersionRepository = contentVersionRepository;
    }

    [HttpPut("save/{contentId}")]
    public IActionResult SaveContent(int contentId, [FromBody] UpdatePageNameRequest request)
    {
        try
        {
            if (request == null || string.IsNullOrWhiteSpace(request.PageName))
            {
                return BadRequest("Page name is required");
            }

            var contentReference = new ContentReference(contentId);

            if (!_contentRepository.TryGet<StartPage>(contentReference, out var startPage))
            {
                return NotFound($"StartPage with ID {contentId} not found");
            }

            var writableStartPage = startPage.CreateWritableClone() as StartPage;
            writableStartPage.Name = request.PageName.Trim();

            _contentRepository.Save(writableStartPage, EPiServer.DataAccess.SaveAction.CheckOut, AccessLevel.NoAccess);

            return Ok(new
            {
                Success = true,
                Message = "Saved successfully",
                ContentId = contentId,
                NewName = writableStartPage.Name
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, "An error occurred while updating the page name");
        }
    }

    [HttpPut("publish/{contentId}")]
    public IActionResult PublishContent(int contentId, [FromBody] UpdatePageNameRequest request)
    {
        try
        {
            if (request == null || string.IsNullOrWhiteSpace(request.PageName))
            {
                return BadRequest("Page name is required");
            }

            var contentReference = new ContentReference(contentId);

            if (!_contentRepository.TryGet<StartPage>(contentReference, out var startPage))
            {
                return NotFound($"StartPage with ID {contentId} not found");
            }

            var writableStartPage = startPage.CreateWritableClone() as StartPage;
            writableStartPage.Name = request.PageName.Trim();

            _contentRepository.Save(writableStartPage, EPiServer.DataAccess.SaveAction.Publish, AccessLevel.NoAccess);

            return Ok(new
            {
                Success = true,
                Message = "Published successfully",
                ContentId = contentId,
                NewName = writableStartPage.Name
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, "An error occurred while updating the page name");
        }
    }
}

public class UpdatePageNameRequest
{
    [Required]
    [StringLength(255, MinimumLength = 1)]
    public string PageName { get; set; }
}