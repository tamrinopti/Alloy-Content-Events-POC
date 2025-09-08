using GcsBlobProvider.GcsBlobProvider;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
using System.Text.Json;

namespace alloy_events_test.GcsBlobProvider
{
    public static class GcsCredentialHelper
    {
        public static GoogleCredential GetGoogleCredentialFromEnvironment(IConfiguration config)
        {
            // Read the service account JSON from the environment variable
            //var json = Environment.GetEnvironmentVariable("GCS_SERVICE_ACCOUNT_JSON");
            var json = config["GCS_SERVICE_ACCOUNT_JSON"];
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException("GCS_SERVICE_ACCOUNT_JSON environment variable is not set.");

            // Optionally: Deserialize to validate structure
            var creds = JsonSerializer.Deserialize<ServiceAccountCredentials>(json);

            // Create GoogleCredential from JSON
            return GoogleCredential.FromJson(json);
        }

        public static StorageClient CreateStorageClient(IConfiguration config)
        {
            var credential = GetGoogleCredentialFromEnvironment(config);
            return StorageClient.Create(credential);
        }
    }
}