using EPiServer.Framework.Blobs;
using EPiServer.Web;
using Google.Apis.Storage.v1.Data;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using System.Collections.Specialized;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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

    public class GcpBlobProvider : BlobProvider
    {
        public const string ProjectIdKey = "projectId";

        public const string BucketKey = "bucket";

        public const string CredentialsPathKey = "credentialsPath";

        public const string ConnectionStringNameKey = "connectionStringName";

        private IGcpBlobContainer _container;
        private GcpBlobContainerFactory _containerFactory;
        private readonly Regex _regexBucketName = new Regex("^[a-z0-9][a-z0-9-_.]{1,61}[a-z0-9]$", RegexOptions.Compiled);
        private readonly IMimeTypeResolver _mimeTypeResolver;
        private readonly IOptions<GcsSettings> _options;

        private IGcpBlobContainer Container => _container ?? (_container = ContainerFactory.GetContainer());

        public GcpBlobContainerFactory ContainerFactory
        {
            get
            {
                return _containerFactory ?? (_containerFactory = new GcpBlobContainerFactory(new GcpBlobContainerClientFactory(_options)));
            }
            set
            {
                _containerFactory = value;
            }
        }

        public GcpBlobProvider(IMimeTypeResolver mimeTypeResolver, IOptions<GcsSettings> options)
        {
            _mimeTypeResolver = mimeTypeResolver;
            _options = options;
        }

        public virtual void Initialize()
        {
            _container = ContainerFactory.GetContainer();
        }

        public override async Task InitializeAsync()
        {
            await Container.CreateIfNotExistAsync();
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
                Container.GetBlob(id).DeleteIfExists();
            }
            else
            {
                Container.DeleteByPrefix(id.Segments[1]);
            }
        }

        public override Blob GetBlob(Uri id)
        {
            ThrowIfNotAbsoluteUri(id);
            GcpBlob blob = Container.GetBlob(id);
            blob.ContentType = _mimeTypeResolver.GetMimeMapping(Path.GetFileName(id.AbsolutePath));
            return blob;
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
