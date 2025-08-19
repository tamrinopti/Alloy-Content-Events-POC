using GcsBlobProvider.GcsBlobProvider;

namespace alloy_events_test.GCSProvider
{
    public class GcpSignedUrlWriteStream: MemoryStream
    {
        private readonly GcpBlob _blob;
        private bool _disposed;

        public GcpSignedUrlWriteStream(GcpBlob blob)
        {
            _blob = blob;
        }
        protected override void Dispose(bool disposing)
        {
            if(!_disposed && disposing)
            {
                Position = 0;
                _blob.UploadFromStream(this);
            }
            _disposed = true;
            base.Dispose(disposing);
        }

        public override void Close()
        {
            if (!_disposed)
            {
                Position = 0;
                _blob.UploadFromStream(this);
            }
            base.Close(); ;
        }
    }
}
