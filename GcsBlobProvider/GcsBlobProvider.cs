using EPiServer.Core;
using EPiServer.Framework.Blobs;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading.Tasks;

namespace GcsBlobProvider.GcsBlobProvider
{
    public class GcsBlobProvider : BlobProvider
    {
        private readonly StorageClient _storageClient;
        private readonly UrlSigner _urlSigner;
        private readonly string _bucketName;
        private readonly TimeSpan _signedUrlExpiry;

        private const string DefaultExtension = ".bin";

        public GcsBlobProvider(IOptions<GcsSettings> options)
        {
            if (options?.Value == null)
                throw new ArgumentNullException(nameof(options), "GCS settings are not configured properly.");

            _bucketName = options.Value.BucketName ?? throw new ArgumentNullException(nameof(options.Value.BucketName));
            var keyPath = options.Value.ServiceAccountKeyPath ?? throw new ArgumentNullException(nameof(options.Value.ServiceAccountKeyPath));

            _storageClient = StorageClient.Create(GoogleCredential.FromFile(keyPath));
            _urlSigner = UrlSigner.FromServiceAccountPath(keyPath);

            _signedUrlExpiry = TimeSpan.FromMinutes(options.Value.SignedUrlDurationMinutes > 0
                ? options.Value.SignedUrlDurationMinutes
                : 60); // fallback default
        }

        public override Task InitializeAsync() => Task.CompletedTask;
        /// <summary>
        /// CMS will call IBlobFactory.CreateBlob() via IContentRepository.Save() while creating a new MediaData content item.
        /// The CreateBlob() from BlobFactory implementation will internally call this method supplied by the cistom bob provider.
        /// This setups destination for the binary file
        /// </summary>
        /// <param name="id"></param>
        /// <param name="extension"></param>
        /// <returns></returns>
        public override Blob CreateBlob(Uri id, string extension)
        {
            var newId = Blob.NewBlobIdentifier(id, extension ?? DefaultExtension);
            return new GcsBlob(newId, _bucketName, _storageClient, _urlSigner, _signedUrlExpiry);
        }

        public override Blob GetBlob(Uri id)
        {
            return new GcsBlob(id, _bucketName, _storageClient, _urlSigner, _signedUrlExpiry);
        }

        public override void Delete(Uri id)
        {
            var objectName = $"media/{id.AbsolutePath.TrimStart('/')}";
            _storageClient.DeleteObject(_bucketName, objectName);
        }
    }
}
