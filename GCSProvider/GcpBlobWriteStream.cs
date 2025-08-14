using GcsBlobProvider.GcsBlobProvider;

namespace alloy_events_test.GcsBlobProvider
{
    internal class GcpBlobWriteStream : MemoryStream
    {
        private readonly GcpBlob _blob;
        private bool _disposed;

        public GcpBlobWriteStream(GcpBlob blob)
        {
            _blob = blob;
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    Position = 0;
                    _blob.UploadFromStream(this);
                }
                _disposed = true;
            }
            base.Dispose(disposing);
        }

        public override void Close()
        {
            if (!_disposed)
            {
                Position = 0;
                _blob.UploadFromStream(this);
            }
            base.Close();
        }
    }
}
