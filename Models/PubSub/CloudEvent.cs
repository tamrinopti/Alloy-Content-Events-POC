namespace alloy_events_test.Models
{
    public class CloudEvent
    {
        public string SpecVersion { get; set; } = "1.0.1";
        public string Type { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public DateTime Time { get; set; }
        public string DataContentType { get; set; } = "application/json";
        public object Data { get; set; } = new();
    }

    public class ContentEventData
    {
        public string? ContentLink { get; set; }
        public string ContentGuid { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public string Language { get; set; } = "en";
        public string? Url { get; set; }
        public DateTime? ModifiedDate { get; set; }
        public int? ContentId { get; set; }
    }
}