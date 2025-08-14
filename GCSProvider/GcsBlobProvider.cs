using EPiServer.Framework.Blobs;
using EPiServer.Web;
using GcsBlobProvider.GcsBlobProvider;
using Microsoft.Extensions.Options;
using Google.Cloud.Storage.V1;
using Google.Apis.Storage.v1.Data;

namespace alloy_events_test.GcsBlobProvider
{
    public class GcpBlobProvider : BlobProvider
    {
        private StorageClient _storageClient;
        private readonly IMimeTypeResolver _mimeTypeResolver;
        private readonly IOptions<GcsSettings> _options;

        public GcpBlobProvider(IMimeTypeResolver mimeTypeResolver, IOptions<GcsSettings> options)
        {
            _mimeTypeResolver = mimeTypeResolver;
            _options = options;
            _storageClient = StorageClient.Create();
        }

        public override async Task InitializeAsync()
        {
            await CreateBucketIfNotExist();
        }

        private async Task CreateBucketIfNotExist()
        {
            try
            {
                await _storageClient.GetBucketAsync(_options.Value.BucketName);
            }
            catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                var bucket = new Bucket { Name = _options.Value.BucketName };
                string projectId = Environment.GetEnvironmentVariable("GCP_PROJECT_ID") ?? "default-project";
                await _storageClient.CreateBucketAsync(projectId, bucket);
            }
        }

        public override Blob CreateBlob(Uri id, string extension)
        {
            ThrowIfNotAbsoluteUri(id);
            return GetBlob(Blob.NewBlobIdentifier(id, extension));
        }

        public override void Delete(Uri id)
        {
            ThrowIfNotAbsoluteUri(id);
            if (id.Segments.Length > 2)
            {
                string objectName = GetObjectName(id);
                try
                {
                    _storageClient.DeleteObject(_options.Value.BucketName, objectName);
                }
                catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Object doesn't exist, ignore
                }
            }
            else
            {
                DeleteByPrefix(id.Segments[1]);
            }
        }

        public override Blob GetBlob(Uri id)
        {
            ThrowIfNotAbsoluteUri(id);
            string objectName = GetObjectName(id);
            var blob = new GcpBlob(_storageClient, _options.Value.BucketName, objectName, id);
            blob.ContentType = _mimeTypeResolver.GetMimeMapping(Path.GetFileName(id.AbsolutePath));
            return blob;
        }

        private void DeleteByPrefix(string prefix)
        {
            if (!prefix.EndsWith("/"))
                prefix += "/";

            var objects = _storageClient.ListObjects(_options.Value.BucketName, prefix);
            foreach (var obj in objects)
            {
                try
                {
                    _storageClient.DeleteObject(_options.Value.BucketName, obj.Name);
                }
                catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Object already deleted, continue
                }
            }
        }

        private string GetObjectName(Uri id)
        {
            return id.AbsolutePath.TrimStart('/');
        }

        private void ThrowIfNotAbsoluteUri(Uri id)
        {
            if (!id.IsAbsoluteUri)
            {
                throw new ArgumentException("Given Uri identifier must be an absolute Uri");
            }
        }
    }

}
