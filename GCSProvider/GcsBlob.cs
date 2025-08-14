 using alloy_events_test.GcsBlobProvider;
using EPiServer.Framework.Blobs;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.FileProviders;
using GcsBlobProvider.GcsBlobProvider;
using Object = Google.Apis.Storage.v1.Data.Object;

namespace GcsBlobProvider.GcsBlobProvider
{
    public class GcpBlob : Blob
    {
        private readonly StorageClient _storageClient;
        private readonly string _bucketName;
        private readonly string _objectName;
        private readonly GcsSettings _settings;

        public string ContentType { get; set; }

        public GcpBlob(StorageClient storageClient, string bucketName, string objectName, Uri id, GcsSettings settings)
            : base(id)
        {
            _storageClient = storageClient;
            _bucketName = bucketName;
            _objectName = objectName;
            _settings = settings;
        }

        public override Stream OpenRead()
        {
            if (_settings.UseSignedUrls)
            {
                var signedUrl = GenerateSignedUrl();
                using var httpClient = new HttpClient();
                var response = httpClient.GetAsync(signedUrl).Result;
                var memoryStream = new MemoryStream();
                response.Content.CopyToAsync(memoryStream).Wait();
                memoryStream.Position = 0;
                return memoryStream;
            }
            else
            {
                var memoryStream = new MemoryStream();
                _storageClient.DownloadObject(_bucketName, _objectName, memoryStream);
                memoryStream.Position = 0;
                return memoryStream;
            }
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

        private string GenerateSignedUrl()
        {
            var credential = Google.Apis.Auth.OAuth2.GoogleCredential.GetApplicationDefault();
            var urlSigner = UrlSigner.FromCredential(credential);
            var duration = TimeSpan.FromMinutes(_settings.SignedUrlDurationMinutes);
            return urlSigner.Sign(_bucketName, _objectName, duration, HttpMethod.Get);
        }
    }
}

