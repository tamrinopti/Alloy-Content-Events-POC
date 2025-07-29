using EPiServer.Framework;
using EPiServer.Framework.Initialization;
using EPiServer.ServiceLocation;
using alloy_events_test.Services;

namespace alloy_events_test
{
    [InitializableModule]
    [ModuleDependency(typeof(EPiServer.Web.InitializationModule))]
    public class ContentEventsModule : IInitializableModule
    {
        public void Initialize(InitializationEngine context)
        {
            var serviceLocator = context.Locate.Advanced;
            var logger = serviceLocator.GetInstance<ILogger<ContentEventsModule>>();
            var config = serviceLocator.GetInstance<IConfiguration>();
            var _eventPublisher = serviceLocator.GetInstance<EventPublisher>();
            var _contentEvents = serviceLocator.GetInstance<IContentEvents>();


            // Check if enabled
            if (!config.GetValue<bool>("EventPublishing:Enabled", true))
            {
                logger.LogInformation("Content event publishing is disabled");
                return;
            }

            try
            {
                // Create 
                _contentEvents.CreatedContent += async (s, e) =>
                    await _eventPublisher.PublishContentEventAsync("created", e.Content, e.ContentLink);
                _contentEvents.CreatingContent += async (s, e) =>
                    await _eventPublisher.PublishContentEventAsync("creating", e.Content, e.ContentLink);

                // Save   
                _contentEvents.SavedContent += async (s, e) =>
                    await _eventPublisher.PublishContentEventAsync("saved", e.Content, e.ContentLink);
                _contentEvents.SavingContent += async (s, e) =>
                    await _eventPublisher.PublishContentEventAsync("saving", e.Content, e.ContentLink);

                // Publish 
                _contentEvents.PublishedContent += async (s, e) =>
                    await _eventPublisher.PublishContentEventAsync("published", e.Content, e.ContentLink);
                _contentEvents.PublishingContent += async (s, e) =>
                    await _eventPublisher.PublishContentEventAsync("publishing", e.Content, e.ContentLink);

                // Delete 
                _contentEvents.DeletedContent += async (s, e) =>
                    await _eventPublisher.PublishContentEventAsync("deleted", null, e.ContentLink);
                _contentEvents.DeletingContent += async (s, e) =>
                    await _eventPublisher.PublishContentEventAsync("deleting", null, e.ContentLink);

                // Move 
                _contentEvents.MovedContent += async (s, e) =>
                    await _eventPublisher.PublishContentEventAsync("moved", e.Content, e.ContentLink);
                _contentEvents.MovingContent += async (s, e) =>
                    await _eventPublisher.PublishContentEventAsync("moving", e.Content, e.ContentLink);

                // Checkin 
                _contentEvents.CheckedInContent += async (s, e) =>
                    await _eventPublisher.PublishContentEventAsync("checkedin", e.Content, e.ContentLink);
                _contentEvents.CheckingInContent += async (s, e) =>
                    await _eventPublisher.PublishContentEventAsync("checkingin", e.Content, e.ContentLink);

                // Checkout 
                _contentEvents.CheckedOutContent += async (s, e) =>
                    await _eventPublisher.PublishContentEventAsync("checkedout", e.Content, e.ContentLink);
                _contentEvents.CheckingOutContent += async (s, e) =>
                    await _eventPublisher.PublishContentEventAsync("checkingout", e.Content, e.ContentLink);

                // Reject 
                _contentEvents.RejectedContent += async (s, e) =>
                    await _eventPublisher.PublishContentEventAsync("rejected", e.Content, e.ContentLink);
                _contentEvents.RejectingContent += async (s, e) =>
                    await _eventPublisher.PublishContentEventAsync("rejecting", e.Content, e.ContentLink);

                logger.LogInformation("Content Events Module initialized - Ready to publish events!");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to initialize Content Events Module");
                throw;
            }
        }

        public void Uninitialize(InitializationEngine context)
        {
        }
    }
}