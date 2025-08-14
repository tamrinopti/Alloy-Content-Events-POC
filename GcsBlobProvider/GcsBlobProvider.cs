using alloy_events_test.GcsBlobProvider;
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


namespace GcsBlobProvider.GcsBlobProvider
{

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
