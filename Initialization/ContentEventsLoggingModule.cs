namespace alloy_events_test;

using alloy_events_test.Models.Pages;
using EPiServer;
using EPiServer.Core;
using EPiServer.Framework;
using EPiServer.Framework.Initialization;
using EPiServer.Logging;
using EPiServer.ServiceLocation;

[InitializableModule]
[ModuleDependency(typeof(EPiServer.Web.InitializationModule))]
public class ContentEventsLoggingModule : IInitializableModule
{
    private static readonly ILogger _logger = LogManager.GetLogger(typeof(ContentEventsLoggingModule));
    private IContentEvents _contentEvents;

    public void Initialize(InitializationEngine context)
    {
        _contentEvents = ServiceLocator.Current.GetInstance<IContentEvents>();

        _contentEvents.CreatedContent += (sender, e) => LogContentEvent("Created", e.Content, e.ContentLink);
        _contentEvents.CreatingContent += (sender, e) => LogContentEvent("Creating", e.Content, e.ContentLink);
        _contentEvents.DeletedContent += (sender, e) => LogContentEvent("Deleted", null, e.ContentLink, $"");
        _contentEvents.DeletingContent += (sender, e) => LogContentEvent("Deleting", null, e.ContentLink, $"");
        _contentEvents.MovedContent += (sender, e) => LogContentEvent("Moved", e.Content, e.ContentLink);
        _contentEvents.MovingContent += (sender, e) => LogContentEvent("Moving", e.Content, e.ContentLink);
        _contentEvents.PublishedContent += (sender, e) => LogContentEvent("Published", e.Content, e.ContentLink);
        _contentEvents.PublishingContent += (sender, e) => LogContentEvent("Publishing", e.Content, e.ContentLink);
        _contentEvents.SavedContent += (sender, e) => LogContentEvent("Saved", e.Content, e.ContentLink);
        _contentEvents.SavingContent += (sender, e) => LogContentEvent("Saving", e.Content, e.ContentLink);
        _contentEvents.CheckedInContent += (sender, e) => LogContentEvent("CheckedIn", e.Content, e.ContentLink);
        _contentEvents.CheckingInContent += (sender, e) => LogContentEvent("CheckingIn", e.Content, e.ContentLink);
        _contentEvents.CheckedOutContent += (sender, e) => LogContentEvent("CheckedOut", e.Content, e.ContentLink);
        _contentEvents.CheckingOutContent += (sender, e) => LogContentEvent("CheckingOut", e.Content, e.ContentLink);
        _contentEvents.RejectedContent += (sender, e) => LogContentEvent("Rejected", e.Content, e.ContentLink);
        _contentEvents.RejectingContent += (sender, e) => LogContentEvent("Rejecting", e.Content, e.ContentLink);

        _logger.Information("Content Events Logging Module initialized successfully");
    }

    public void Uninitialize(InitializationEngine context)
    {
        _logger.Information("Content Events Logging Module uninitialized");
    }

    private void LogContentEvent(string eventType, IContent content, ContentReference contentLink, string additionalInfo = null)
    {
        try
        {
            var contentType = content?.GetOriginalType()?.Name ?? "Unknown";
            var contentName = content?.Name ?? "Unknown";
            var contentId = contentLink?.ID ?? 0;
            var workId = contentLink?.WorkID ?? 0;

            var logMessage = $"[CONTENT EVENT] {eventType} - " +
                           $"Type: {contentType}, " +
                           $"Name: '{contentName}', " +
                           $"ID: {contentId}";

            if (workId > 0)
            {
                logMessage += $", WorkID: {workId}";
            }

            if (!string.IsNullOrEmpty(additionalInfo))
            {
                logMessage += $", {additionalInfo}";
            }

            var currentUser = EPiServer.Security.PrincipalInfo.CurrentPrincipal?.Identity?.Name;
            if (!string.IsNullOrEmpty(currentUser))
            {
                logMessage += $", User: {currentUser}";
            }

            _logger.Information(logMessage);
        }
        catch (Exception ex)
        {
            _logger.Error($"Error logging content event {eventType}: {ex.Message}", ex);
        }
    }
}
