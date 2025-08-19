 using alloy_events_test.GcsBlobProvider;
using EPiServer.Framework.Blobs;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.FileProviders;
using GcsBlobProvider.GcsBlobProvider;
using Object = Google.Apis.Storage.v1.Data.Object;
using Google.Apis.Storage.v1.Data;
using alloy_events_test.GCSProvider;

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

                //Console.WriteLine($"signed url generated for downloading {_objectName} is {signedUrl}");

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
            if(_settings.UseSignedUrls)
            {
                return new GcpSignedUrlWriteStream(this);
            }
            else
            {
                return new GcpBlobWriteStream(this);
            }
                
        }

        public override void Write(Stream data)
        {
            if(_settings.UseSignedUrls)
            {
                UploadViaSignedUrl(data);
            }
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

        private void UploadViaSignedUrl(Stream data)
        {
            try
            {
                var signedUrl = GenerateSignedUrl(HttpMethod.Put);

                Console.WriteLine($"signed url generated for uploading {_objectName} is {signedUrl}\n");

                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromMinutes(10); // Extended timeout for large files

                // Create content with proper headers
                var content = new StreamContent(data);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(ContentType ?? "application/octet-stream");
                content.Headers.ContentLength = data.Length;

                var response = httpClient.PutAsync(signedUrl, content).Result;
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to upload via signed URL to {_objectName}: {ex.Message}", ex);
            }
        }

        private string GenerateSignedUrl()
        {
            var credential = Google.Apis.Auth.OAuth2.GoogleCredential.GetApplicationDefault();
            var urlSigner = UrlSigner.FromCredential(credential);
            var duration = TimeSpan.FromMinutes(_settings.SignedUrlDurationMinutes);
            return urlSigner.Sign(_bucketName, _objectName, duration, HttpMethod.Get);
        }

        private string GenerateSignedUrl(HttpMethod httpMethod)
        {
            try
            {
                var credential = Google.Apis.Auth.OAuth2.GoogleCredential.GetApplicationDefault();
                var urlSigner = UrlSigner.FromCredential(credential);
                var duration = TimeSpan.FromMinutes(_settings.SignedUrlDurationMinutes);

                // Use the correct method signature for UrlSigner.Sign
                var requestHeaders = new Dictionary<string, IEnumerable<string>>();

                // Add content type for uploads
                if (httpMethod == HttpMethod.Put && !string.IsNullOrEmpty(ContentType))
                {
                    requestHeaders["Content-Type"] = new[] { ContentType };
                }

                return urlSigner.Sign(
                    bucket: _bucketName,
                    objectName: _objectName,
                    duration: duration,
                    httpMethod: httpMethod
                );
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to generate signed URL for {httpMethod} operation on {_objectName}: {ex.Message}", ex);
            }
        }

        // Public method to generate signed URLs for external use (e.g., direct browser uploads)
        public string GetSignedUploadUrl(int durationMinutes = 0)
        {
            var duration = durationMinutes > 0 ? durationMinutes : _settings.SignedUrlDurationMinutes;
            var tempSettings = new GcsSettings { SignedUrlDurationMinutes = duration };

            return GenerateSignedUrl(HttpMethod.Put);
        }

        public string GetSignedDownloadUrl(int durationMinutes = 0)
        {
            var duration = durationMinutes > 0 ? durationMinutes : _settings.SignedUrlDurationMinutes;

            return GenerateSignedUrl(HttpMethod.Get);
        }
    }
}

