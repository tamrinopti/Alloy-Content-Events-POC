using Google.Cloud.Storage.V1;
using EPiServer.Framework.Blobs;

namespace GcsBlobProvider.GcsBlobProvider
{
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


    public class GcsBlob : Blob
    {
        private readonly StorageClient _storageClient;
        private readonly string _bucketName;

        public GcsBlob(Uri id, string bucketName, StorageClient storageClient)
            : base(id)
        {
            _storageClient = storageClient;
            _bucketName = bucketName;
        }

        public override Stream OpenRead()
        {
            var objectName = $"media/{ID.AbsolutePath.TrimStart('/')}";
            var stream = new MemoryStream();
            _storageClient.DownloadObject(_bucketName, objectName, stream);
            stream.Position = 0;
            return stream;
        }

        public string GetPublicUrl()
        {
            // Public access URL format
            return $"https://storage.googleapis.com/{_bucketName}/{Uri.EscapeDataString($"media/{ID.AbsolutePath.TrimStart('/')}")}";
        }
    }

}

