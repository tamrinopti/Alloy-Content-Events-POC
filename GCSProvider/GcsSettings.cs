namespace GcsBlobProvider.GcsBlobProvider
{
    public class GcsSettings
    {
        public string BucketName { get; set; }
        public int SignedUrlDurationMinutes { get; set; } = 60;
        public bool UseSignedUrls { get; set; } = false;
    }
}
