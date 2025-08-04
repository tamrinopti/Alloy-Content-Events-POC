using System.Text.Json;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using alloy_events_test.Models;

namespace alloy_events_test.Services
{
    public class EventPublisher : IDisposable
    {
        private readonly ILogger<EventPublisher> _logger;
        private readonly PublisherClient _publisher;
        private readonly bool _useConsole;
        private readonly string _sourceUrl;

        public EventPublisher(ILogger<EventPublisher> logger, IConfiguration config)
        {
            _logger = logger;
            _useConsole = config.GetValue<bool>("EventPublishing:UseConsolePublisher");
            _sourceUrl = config.GetValue<string>("EventPublishing:SourceUrl") ?? "https://cms.local";

            if (!_useConsole)
            {
                try
                {
                    var projectId = config.GetValue<string>("EventPublishing:GcpProjectId");
                    var topicName = config.GetValue<string>("EventPublishing:PubSubTopicName") ?? "cms";

                    if (!string.IsNullOrEmpty(projectId))
                    {
                        var topic = TopicName.FromProjectTopic(projectId, topicName);
                        _publisher = PublisherClient.Create(topic);
                        _logger.LogInformation("GCP Pub/Sub publisher initialized");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize GCP publisher, falling back to console");
                    _useConsole = true;
                }
            }
        }

        public async Task PublishContentEventAsync(string eventType, IContent? content, ContentReference contentLink)
        {
            try
            {
                var cloudEvent = CreateCloudEvent(eventType, content, contentLink);

                if (_useConsole || _publisher == null)
                {
                    await PublishToConsole(cloudEvent);
                }
                else
                {
                    await PublishToGcp(cloudEvent);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish {EventType} event for content {ContentId}",
                    eventType, contentLink?.ID);
            }
        }

        private CloudEvent CreateCloudEvent(string eventType, IContent? content, ContentReference contentLink)
        {
            var contentType = content?.GetOriginalType()?.Name ?? "Unknown";
            var contentName = content?.Name ?? "Unknown";
            var contentGuid = content?.ContentGuid ?? Guid.Empty;

            var eventData = new ContentEventData
            {
                ContentLink = contentLink?.ToString(),
                ContentGuid = contentGuid.ToString(),
                Name = contentName,
                ContentType = contentType,
                Language = content is ILocalizable localizable ? localizable.Language?.Name : "en",
                Url = content is PageData page ? page.LinkURL : null,
                ModifiedDate = GetModifiedDate(content),
                ContentId = contentLink?.ID
            };

            return new CloudEvent
            {
                Type = $"com.youversionapi.cms.content.{eventType}.v1",
                Source = _sourceUrl,
                Subject = contentLink?.ToString() ?? contentGuid.ToString(),
                Id = Guid.NewGuid().ToString(),
                Time = DateTime.UtcNow,
                Data = eventData
            };
        }

        private DateTime? GetModifiedDate(IContent? content)
        {
            if (content is IChangeTrackable changeTrackable)
                return changeTrackable.Changed;

            if (content is IVersionable versionable)
                return versionable.StartPublish;

            return DateTime.UtcNow;
        }

        private async Task PublishToConsole(CloudEvent cloudEvent)
        {
            var json = JsonSerializer.Serialize(cloudEvent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });

            _logger.LogInformation("Event Type: {EventType}", cloudEvent.Type);
            _logger.LogInformation("Event Data:\n{EventData}", json);
        }

        private async Task PublishToGcp(CloudEvent cloudEvent)
        {
            var message = new PubsubMessage();

            message.Attributes["specversion"] = cloudEvent.SpecVersion;
            message.Attributes["type"] = cloudEvent.Type;
            message.Attributes["source"] = cloudEvent.Source;
            message.Attributes["subject"] = cloudEvent.Subject;
            message.Attributes["id"] = cloudEvent.Id;
            message.Attributes["time"] = cloudEvent.Time.ToString("yyyy-MM-ddTHH:mm:ssZ");
            message.Attributes["datacontenttype"] = cloudEvent.DataContentType;

            var jsonData = JsonSerializer.Serialize(cloudEvent.Data, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            message.Data = ByteString.CopyFromUtf8(jsonData);

            var messageId = await _publisher.PublishAsync(message);
            _logger.LogInformation("Published {EventType} to GCP with ID: {MessageId}",
                cloudEvent.Type, messageId);
        }

        public void Dispose()
        {
            _publisher.ShutdownAsync(TimeSpan.Zero).ConfigureAwait(true);
        }
    }
}