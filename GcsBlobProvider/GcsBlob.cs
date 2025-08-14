using EPiServer.Framework.Blobs;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.FileProviders;
using Object = Google.Apis.Storage.v1.Data.Object;

namespace GcsBlobProvider.GcsBlobProvider
{

    public class GcpBlob : Blob
    {
        private readonly StorageClient _storageClient;
        private readonly string _bucketName;
        private readonly string _objectName;

        public string ContentType { get; set; }

        public GcpBlob(StorageClient storageClient, string bucketName, string objectName, Uri id)
            : base(id)
        {
            _storageClient = storageClient;
            _bucketName = bucketName;
            _objectName = objectName;
        }

        public override Stream OpenRead()
        {
            var memoryStream = new MemoryStream();
            _storageClient.DownloadObject(_bucketName, _objectName, memoryStream);
            memoryStream.Position = 0;
            return memoryStream;
        }

        public override Stream OpenWrite()
        {
            return new GcpBlobWriteStream(this);
        }

        public override void Write(Stream data)
        {
            var uploadObject = new Object
            {
                Bucket = _bucketName,
                Name = _objectName,
                ContentType = ContentType
            };

            _storageClient.UploadObject(uploadObject, data);
        }

        public virtual void DeleteIfExists()
        {
            try
            {
                _storageClient.DeleteObject(_bucketName, _objectName);
            }
            catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Object doesn't exist, which is fine for DeleteIfExists
            }
        }
        public override async Task<IFileInfo> AsFileInfoAsync(DateTimeOffset? lastModified = null)
        {
            var memoryStream = new MemoryStream();
            await _storageClient.DownloadObjectAsync(_bucketName, _objectName, memoryStream);
            memoryStream.Position = 0;

            return new GcpBlobFileInfo(_objectName, memoryStream, lastModified);
        }

        internal void UploadFromStream(Stream stream)
        {
            stream.Position = 0;
            Write(stream);
        }
    }

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
    //public class GcsBlob : Blob
    //{
    //    private readonly string _bucketName;
    //    private readonly StorageClient _storageClient;
    //    private readonly UrlSigner _urlSigner;
    //    private readonly TimeSpan _signedUrlExpiry;

    //    private const string GcsPathPrefix = "media/";

    //    public GcsBlob(
    //        Uri id,
    //        string bucketName,
    //        StorageClient storageClient,
    //        UrlSigner urlSigner,
    //        TimeSpan signedUrlExpiry)
    //        : base(id)
    //    {
    //        _bucketName = bucketName ?? throw new ArgumentNullException(nameof(bucketName));
    //        _storageClient = storageClient ?? throw new ArgumentNullException(nameof(storageClient));
    //        _urlSigner = urlSigner ?? throw new ArgumentNullException(nameof(urlSigner));
    //        _signedUrlExpiry = signedUrlExpiry;
    //    }

    //    private string ObjectName => $"{GcsPathPrefix}{ID.AbsolutePath.TrimStart('/')}";

    //    public override Stream OpenRead()
    //    {
    //        var memoryStream = new MemoryStream();
    //        _storageClient.DownloadObject(_bucketName, ObjectName, memoryStream);
    //        memoryStream.Position = 0;
    //        return memoryStream;
    //    }

    //    /// <summary>
    //    /// CMS will call it during the upload od data/media file.
    //    /// CMS will stream/write the uploaded data/media file's byte into the blob generated in CreateBlob method of the BlobProvider
    //    /// </summary>
    //    /// <returns></returns>
    //    public override Stream OpenWrite()
    //    {
    //        var temp = new MemoryStream();
    //        var trackable = new TrackableStream(temp); // from EPiServer.Framework.Blobs
    //        //upload to GCS whn the stream is closed
    //        trackable.Closing += (source, _) =>
    //        {
    //            var ms = (MemoryStream)source;
    //            ms.Position = 0;
    //            _storageClient.UploadObject(_bucketName, ObjectName, null, ms);
    //        };
    //        return trackable;
    //    }

    //    public Uri GetSignedUrl()
    //    {
    //        string signedString = _urlSigner.Sign(
    //            bucket: _bucketName,
    //            objectName: ObjectName,
    //            duration: _signedUrlExpiry,
    //            httpMethod: HttpMethod.Get);
    //        return new Uri(signedString);
    //    }
    //}



}

