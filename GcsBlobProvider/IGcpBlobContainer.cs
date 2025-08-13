namespace GcsBlobProvider.GcsBlobProvider;

public interface IGcpBlobContainer
{
    GcpBlob GetBlob(Uri blobUri);

    void DeleteByPrefix(string directory);

    Task CreateIfNotExistAsync();
}
