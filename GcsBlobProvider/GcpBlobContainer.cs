using GcsBlobProvider.GcsBlobProvider;
using Google.Apis.Storage.v1.Data;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text;

namespace alloy_events_test.GcsBlobProvider
{
    public class GcpBlobContainer : IGcpBlobContainer
    {
        private readonly StorageClient _storageClient;
        private readonly IOptions<GcsSettings> _options;

        public GcpBlobContainer(IOptions<GcsSettings> options)
        {
            _storageClient = StorageClient.Create();
            _options = options;
        }

        public async Task CreateIfNotExistAsync()
        {
            try
            {
                await _storageClient.GetBucketAsync(_options.Value.BucketName);
            }
            catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                await _storageClient.CreateBucketAsync(_options.Value.BucketName, new Bucket
                {
                    Name = _options.Value.BucketName,
                    Location = "US"
                });
            }
        }

        public GcpBlob GetBlob(Uri id)
        {
            string objectName = GetObjectNameFromUri(id);

            return new GcpBlob(_storageClient, _options.Value.BucketName, objectName, id);
        }

        public void DeleteByPrefix(string prefix)
        {
            var objects = _storageClient.ListObjects(_options.Value.BucketName, prefix);

            foreach (var obj in objects)
            {
                _storageClient.DeleteObject(_options.Value.BucketName, obj.Name);
            }
        }

        private string GetObjectNameFromUri(Uri uri)
        {
            if (uri.Segments.Length > 1)
            {
                return string.Join("", uri.Segments, 1, uri.Segments.Length - 1).TrimStart('/');
            }

            return uri.AbsolutePath.TrimStart('/');
        }
    }

    public class GcpBlobContainerFactory
    {
        private readonly GcpBlobContainerClientFactory _clientFactory;

        public GcpBlobContainerFactory(GcpBlobContainerClientFactory clientFactory)
        {
            _clientFactory = clientFactory;
        }

        public virtual IGcpBlobContainer GetContainer()
        {
            return new DefaultGcpBlobContainer(_clientFactory.CreateClient(), _clientFactory.GetBucketName());
        }
    }

    internal class DefaultGcpBlobContainer : IGcpBlobContainer
    {
        private const string MetaDataCreatedDate = "EPiServerGCPCreatedDate";
        private const string MetaDataCreatedByMachine = "EPiServerGCPCreatedByMachine";
        private readonly StorageClient _storageClient;
        private readonly string _bucketName;

        public DefaultGcpBlobContainer(StorageClient storageClient, string bucketName)
        {
            _storageClient = storageClient ?? throw new ArgumentNullException(nameof(storageClient));
            _bucketName = bucketName ?? throw new ArgumentNullException(nameof(bucketName));
        }

        public GcpBlob GetBlob(Uri blobUri)
        {
            string objectName = GetGcpRelativeAddress(blobUri);
            return new GcpBlob(_storageClient, _bucketName, objectName, blobUri);
        }

        public void DeleteByPrefix(string directoryName)
        {
            if (string.IsNullOrEmpty(directoryName))
            {
                throw new ArgumentNullException(nameof(directoryName));
            }

            // Ensure directory name ends with '/' for proper prefix matching
            if (!directoryName.EndsWith("/"))
            {
                directoryName += "/";
            }

            // List all objects with the given prefix
            var objects = _storageClient.ListObjects(_bucketName, directoryName);

            foreach (var obj in objects)
            {
                try
                {
                    _storageClient.DeleteObject(_bucketName, obj.Name);
                }
                catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Object already deleted, continue
                }
            }
        }

        public async Task CreateIfNotExistAsync()
        {
            try
            {
                // Check if bucket exists
                await _storageClient.GetBucketAsync(_bucketName);
            }
            catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Bucket doesn't exist, create it
                string createdDate = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss", CultureInfo.InvariantCulture);
                string machineName = ConvertToAscii(Environment.MachineName);

                var bucket = new Bucket
                {
                    Name = _bucketName,
                    Labels = new Dictionary<string, string>
                    {
                        { SanitizeLabel(MetaDataCreatedDate), SanitizeLabel(createdDate) },
                        { SanitizeLabel(MetaDataCreatedByMachine), SanitizeLabel(machineName) }
                    }
                };

                // Get project ID from storage client or options
                string projectId = GetProjectId();
                await _storageClient.CreateBucketAsync(projectId, bucket);
            }
        }

        private string GetGcpRelativeAddress(Uri id)
        {
            string absolutePath = id.AbsolutePath;
            // Remove leading slash
            return absolutePath.Substring(1, absolutePath.Length - 1);
        }

        private string ConvertToAscii(string stringToConvert)
        {
            return new string((from b in Encoding.ASCII.GetBytes(stringToConvert)
                               select (char)b).ToArray());
        }

        /// <summary>
        /// Sanitizes a label to meet GCP requirements (lowercase, alphanumeric, hyphens, underscores)
        /// </summary>
        private string SanitizeLabel(string label)
        {
            if (string.IsNullOrEmpty(label))
                return label;

            // Convert to lowercase and replace invalid characters with underscores
            return System.Text.RegularExpressions.Regex.Replace(
                label.ToLowerInvariant(),
                @"[^a-z0-9_-]",
                "_"
            ).Substring(0, Math.Min(label.Length, 63)); // GCP labels max 63 chars
        }

        private string GetProjectId()
        {
            // This would need to be passed or retrieved from configuration
            // For now, returning a placeholder
            return Environment.GetEnvironmentVariable("GCP_PROJECT_ID") ?? "default-project";
        }
    }

    public class GcpBlobContainerClientFactory
    {
        private readonly IOptions<GcsSettings> _options;
        private StorageClient _storageClient;

        public GcpBlobContainerClientFactory(IOptions<GcsSettings> options)
        {
            _options = options;
        }

        public StorageClient CreateClient()
        {
            _storageClient = StorageClient.Create();

            return _storageClient;
        }

        public string GetBucketName()
        {
            return _options.Value.BucketName;
        }
    }
}
