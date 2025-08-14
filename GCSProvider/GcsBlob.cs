using alloy_events_test.GcsBlobProvider;
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
}

