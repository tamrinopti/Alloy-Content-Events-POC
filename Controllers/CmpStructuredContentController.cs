using Microsoft.AspNetCore.Mvc;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using alloy_events_test.CMPIntegration;
using System.ComponentModel.DataAnnotations;

namespace alloy_events_test.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CmpStructuredContentController : ControllerBase
    {
        private readonly ICmpStructuredContentService _cmpStructuredContentService;
        private readonly IContentRepository _contentRepository;
        private readonly IContentLoader _contentLoader;
        private readonly ILogger<CmpStructuredContentController> _logger;

        public CmpStructuredContentController(
            ICmpStructuredContentService cmpStructuredContentService,
            IContentRepository contentRepository,
            IContentLoader contentLoader,
            ILogger<CmpStructuredContentController> logger)
        {
            _cmpStructuredContentService = cmpStructuredContentService ?? throw new ArgumentNullException(nameof(cmpStructuredContentService));
            _contentRepository = contentRepository ?? throw new ArgumentNullException(nameof(contentRepository));
            _contentLoader = contentLoader ?? throw new ArgumentNullException(nameof(contentLoader));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Creates and publishes structured content in CMP from Optimizely CMS content
        /// </summary>
        /// <param name="request">The request containing content details</param>
        /// <returns>The CMP content ID of the created structured content</returns>
        [HttpPost("create")]
        public async Task<IActionResult> CreateStructuredContent([FromBody] CreateStructuredContentRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                _logger.LogInformation("🔄 Creating structured content in CMP for content reference {ContentRef}", request.ContentReference);

                // Parse content reference
                if (!ContentReference.TryParse(request.ContentReference, out var contentRef))
                {
                    return BadRequest($"Invalid content reference: {request.ContentReference}");
                }

                // Get the content from Optimizely CMS
                var cmsContent = _contentRepository.Get<IContent>(contentRef);
                if (cmsContent == null)
                {
                    return NotFound($"Content with reference {request.ContentReference} not found");
                }

                var contentUrl = GetContentEditUrl(cmsContent);

                // Create and publish structured content in CMP
                var cmpContentId = await _cmpStructuredContentService.CreateAndPublishStructuredContentAsync(
                    cmsContent, 
                    contentRef, 
                    contentUrl, 
                    request.FolderId);

                _logger.LogInformation("✅ Successfully created structured content in CMP with ID {CmpContentId}", cmpContentId);

                return Ok(new CreateStructuredContentResponse
                {
                    Success = true,
                    CmpContentId = cmpContentId,
                    Message = $"Successfully created structured content '{cmsContent.Name}' in CMP",
                    ContentReference = request.ContentReference,
                    ContentName = cmsContent.Name,
                    ContentTypeName = cmsContent.GetOriginalType().Name
                });
            }
            catch (CmpApiException ex)
            {
                _logger.LogError(ex, "❌ CMP API error while creating structured content for {ContentRef}", request.ContentReference);
                return BadRequest(new CreateStructuredContentResponse
                {
                    Success = false,
                    Message = "Failed to create structured content in CMP",
                    ErrorDetails = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error while creating structured content for {ContentRef}", request.ContentReference);
                return StatusCode(500, new CreateStructuredContentResponse
                {
                    Success = false,
                    Message = "An unexpected error occurred",
                    ErrorDetails = ex.Message
                });
            }
        }

        /// <summary>
        /// Creates structured content in CMP from Optimizely CMS content by content GUID
        /// </summary>
        /// <param name="request">The request containing content GUID and details</param>
        /// <returns>The CMP content ID of the created structured content</returns>
        [HttpPost("create-by-guid")]
        public async Task<IActionResult> CreateStructuredContentByGuid([FromBody] CreateStructuredContentByGuidRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                _logger.LogInformation("🔄 Creating structured content in CMP for content GUID {ContentGuid}", request.ContentGuid);

                // Get content by GUID using IContentLoader
                if (!_contentLoader.TryGet(request.ContentGuid, out IContent cmsContent))
                {
                    return NotFound($"Content with GUID {request.ContentGuid} not found");
                }

                var contentRef = cmsContent.ContentLink;

                // Create and publish structured content in CMP
                var cmpContentId = await _cmpStructuredContentService.CreateAndPublishStructuredContentAsync(
                    cmsContent, 
                    contentRef, 
                    request.ContentUrl, 
                    request.FolderId);

                _logger.LogInformation("✅ Successfully created structured content in CMP with ID {CmpContentId}", cmpContentId);

                return Ok(new CreateStructuredContentResponse
                {
                    Success = true,
                    CmpContentId = cmpContentId,
                    Message = $"Successfully created structured content '{cmsContent.Name}' in CMP",
                    ContentReference = contentRef.ToString(),
                    ContentName = cmsContent.Name,
                    ContentTypeName = cmsContent.GetOriginalType().Name
                });
            }
            catch (CmpApiException ex)
            {
                _logger.LogError(ex, "❌ CMP API error while creating structured content for GUID {ContentGuid}", request.ContentGuid);
                return BadRequest(new CreateStructuredContentResponse
                {
                    Success = false,
                    Message = "Failed to create structured content in CMP",
                    ErrorDetails = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error while creating structured content for GUID {ContentGuid}", request.ContentGuid);
                return StatusCode(500, new CreateStructuredContentResponse
                {
                    Success = false,
                    Message = "An unexpected error occurred",
                    ErrorDetails = ex.Message
                });
            }
        }

        /// <summary>
        /// Gets content information from Optimizely CMS
        /// </summary>
        /// <param name="contentReference">The content reference</param>
        /// <returns>Content information</returns>
        [HttpGet("content-info/{contentReference}")]
        public IActionResult GetContentInfo(string contentReference)
        {
            try
            {
                if (!ContentReference.TryParse(contentReference, out var contentRef))
                {
                    return BadRequest($"Invalid content reference: {contentReference}");
                }

                var cmsContent = _contentRepository.Get<IContent>(contentRef);
                if (cmsContent == null)
                {
                    return NotFound($"Content with reference {contentReference} not found");
                }

                var publishedContent = cmsContent as IVersionable;
                var changeTrackable = cmsContent as IChangeTrackable;
                var localizable = cmsContent as ILocalizable;

                return Ok(new
                {
                    ContentReference = contentReference,
                    ContentGuid = cmsContent.ContentGuid,
                    Name = cmsContent.Name,
                    ContentTypeName = cmsContent.GetOriginalType().Name,
                    ContentTypeFullName = cmsContent.GetOriginalType().FullName,
                    Language = localizable?.Language?.Name,
                    StartPublish = publishedContent?.StartPublish,
                    StopPublish = publishedContent?.StopPublish,
                    Created = changeTrackable?.Created,
                    Changed = changeTrackable?.Changed
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting content info for {ContentReference}", contentReference);
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Gets content information from Optimizely CMS by GUID
        /// </summary>
        /// <param name="contentGuid">The content GUID</param>
        /// <returns>Content information</returns>
        [HttpGet("content-info-by-guid/{contentGuid:guid}")]
        public IActionResult GetContentInfoByGuid(Guid contentGuid)
        {
            try
            {
                if (!_contentLoader.TryGet(contentGuid, out IContent cmsContent))
                {
                    return NotFound($"Content with GUID {contentGuid} not found");
                }

                var publishedContent = cmsContent as IVersionable;
                var changeTrackable = cmsContent as IChangeTrackable;
                var localizable = cmsContent as ILocalizable;

                return Ok(new
                {
                    ContentReference = cmsContent.ContentLink.ToString(),
                    ContentGuid = cmsContent.ContentGuid,
                    Name = cmsContent.Name,
                    ContentTypeName = cmsContent.GetOriginalType().Name,
                    ContentTypeFullName = cmsContent.GetOriginalType().FullName,
                    Language = localizable?.Language?.Name,
                    StartPublish = publishedContent?.StartPublish,
                    StopPublish = publishedContent?.StopPublish,
                    Created = changeTrackable?.Created,
                    Changed = changeTrackable?.Changed
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting content info for GUID {ContentGuid}", contentGuid);
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        private string GetContentEditUrl(IContent content)
        {
            return $"https://localhost:5000/episerver/cms/admin#context=epi.cms.contentdata:///{content.ContentLink}";
        }
    }

    // Request/Response DTOs
    public class CreateStructuredContentRequest
    {
        [Required]
        public string ContentReference { get; set; }

        //[Required]
        //public string ContentUrl { get; set; }

        public string FolderId { get; set; }
    }

    public class CreateStructuredContentByGuidRequest
    {
        [Required]
        public Guid ContentGuid { get; set; }

        [Required]
        public string ContentUrl { get; set; }

        public string FolderId { get; set; }
    }

    public class CreateStructuredContentResponse
    {
        public bool Success { get; set; }
        public string CmpContentId { get; set; }
        public string Message { get; set; }
        public string ErrorDetails { get; set; }
        public string ContentReference { get; set; }
        public string ContentName { get; set; }
        public string ContentTypeName { get; set; }
    }
}