using Microsoft.Extensions.FileProviders;

namespace alloy_events_test.GcsBlobProvider
{
    public class GcpBlobFileInfo : IFileInfo
    {
        private readonly Stream _stream;

        public GcpBlobFileInfo(string name, Stream stream, DateTimeOffset? lastModified)
        {
            Name = name;
            _stream = stream;
            LastModified = lastModified ?? DateTimeOffset.UtcNow;
            Length = stream.Length;
            PhysicalPath = null;
            IsDirectory = false;
            Exists = true;
        }

        public Stream CreateReadStream() => _stream;
        public bool Exists { get; }
        public long Length { get; }
        public string PhysicalPath { get; }
        public string Name { get; }
        public DateTimeOffset LastModified { get; }
        public bool IsDirectory { get; }
    }
}
