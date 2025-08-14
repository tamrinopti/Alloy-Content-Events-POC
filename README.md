# Optimizely CMS 12 Content Events Demo

A demonstration project showcasing content event logging in Optimizely CMS 12, featuring a custom Google Cloud Storage blob provider.

## Features

- **Content Events Logging**: Comprehensive logging of all content lifecycle events (Create, Save, Publish, Delete, Move, etc.)
- **API Endpoints**: REST API to save and publish StartPage names and trigger content events
- **Event Monitoring**: Real-time logging of content operations with user information
- **GCS Blob Provider**: Custom implementation for storing media files in Google Cloud Storage

## Content Events Monitored

The `ContentEventsLoggingModule` logs the following events:
- Creating/Created
- Saving/Saved
- Publishing/Published
- Deleting/Deleted
- Moving/Moved
- CheckingIn/CheckedIn
- CheckingOut/CheckedOut
- Rejecting/Rejected

## API Usage

### Save StartPage Name (Draft)
**Endpoint**: `PUT /api/StartPageAPI/save/{contentId}`

**Example**:
```bash
PUT /api/StartPageAPI/save/5
Content-Type: application/json

{
  "pageName": "My Updated Page Name"
}
```

### Publish StartPage Name
**Endpoint**: `PUT /api/StartPageAPI/publish/{contentId}`

**Example**:
```bash
PUT /api/StartPageAPI/publish/5
Content-Type: application/json

{
  "pageName": "My Published Page Name"
}
```

## Login Credentials

To access the CMS admin interface:
- **Username**: `sysadmin`
- **Password**: `Sysadmin@1234`

## Quick Test

1. Start the application
2. Login to CMS admin (if needed) using credentials above
3. Access Swagger UI at `/swagger`
4. Use either:
   - `PUT /api/StartPageAPI/save/5` to save as draft
   - `PUT /api/StartPageAPI/publish/5` to publish immediately
5. Update with any text (e.g., "Test Page Update")
6. Check logs to see content events being triggered

## Log Output Example

### For Save (Draft) Operation:
```
[CONTENT EVENT] Saving - Type: StartPage, Name: 'Test Page Update', ID: 5, User: admin
[CONTENT EVENT] Saved - Type: StartPage, Name: 'Test Page Update', ID: 5, User: admin
[START PAGE EVENT] Saved - StartPage specific data logged for 'Test Page Update'
```

### For Publish Operation:
```
[CONTENT EVENT] Publishing - Type: StartPage, Name: 'Test Page Update', ID: 5, User: admin
[CONTENT EVENT] Published - Type: StartPage, Name: 'Test Page Update', ID: 5, User: admin
[START PAGE EVENT] Published - StartPage specific data logged for 'Test Page Update'
```

## Requirements

- Optimizely CMS 12
- .NET 6+
- Valid StartPage content with ID 5 (or adjust the test ID accordingly)
- Google Cloud Project with Storage API enabled (for GCS provider)

## Cloud Storage Implementation

### GCS (Google Cloud Storage) Provider

A custom blob storage implementation that allows Episerver CMS to store media files and blobs in Google Cloud Storage instead of the local file system or database.

### Core Components

Simple implementation with only 5 files:

1. **GcpBlobProvider** (`GcsBlobProvider.cs`) - Main provider with direct GCS client integration
2. **GcpBlob** (`GcsBlob.cs`) - Individual blob representation  
3. **GcsSettings** (`GcsSettings.cs`) - Configuration class
4. **GcpBlobWriteStream** (`GcpBlobWriteStream.cs`) - Write stream for GCS uploads
5. **GcpBlobFileInfo** (`GcpBlobFileInfo.cs`) - File metadata implementation

### File Structure

```
GCSProvider/
├── GcsBlobProvider.cs      # Main provider
├── GcsBlob.cs             # Individual blob representation
├── GcsSettings.cs         # Configuration class
├── GcpBlobWriteStream.cs  # Write stream implementation
└── GcpBlobFileInfo.cs     # File info implementation
```

### Configuration

#### Settings Class (`GcsSettings.cs`)
```csharp
public class GcsSettings
{
    public string BucketName { get; set; }
    public string ServiceAccountKeyPath { get; set; }
    public int SignedUrlDurationMinutes { get; set; } = 60;
}
```

#### Configuration in `appsettings.json`
```json
{
  "GcsSettings": {
    "BucketName": "storage-bucket-u-version",
    "SignedUrlDurationMinutes": 60,
    "GcpProjectId": "rebuild-cms-sandbox"
  }
}
```

#### Service Registration in `Startup.cs`
```csharp
// Register GCS settings
services.Configure<GcsSettings>(Configuration.GetSection("GcsSettings"));

// Register the GCS blob provider as default
services.AddBlobProvider<GcpBlobProvider>("GcsBlobProvider", defaultProvider: true);
```

### Dependencies

The implementation requires the following NuGet packages:
- `Google.Cloud.Storage.V1` (v4.13.0) - Google Cloud Storage client library
- `EPiServer.CMS` (v12.29.0) - Episerver CMS framework

### Authentication

The provider uses Google Cloud Storage client with default authentication:
```csharp
_storageClient = StorageClient.Create();
```

This relies on:
- Service account key files
- Environment variables (GOOGLE_APPLICATION_CREDENTIALS)
- Google Cloud SDK default credentials

### Configuration Requirements

1. **Google Cloud Project**: Active GCP project with Storage API enabled
2. **Service Account**: Service account with Storage Admin or Object Admin permissions
3. **Authentication**: Set `GOOGLE_APPLICATION_CREDENTIALS` environment variable or use default credentials
4. **Bucket Configuration**: Configure bucket name in `appsettings.json`

### Usage Workflow

1. **Initialization**: Provider initializes and creates GCS bucket if needed
2. **Upload**: Media files uploaded through CMS are stored in GCS with URI-based object names
3. **Metadata Storage**: CMS stores blob URI in content database, linking to GCS object
4. **Retrieval**: CMS requests blobs by URI, provider maps URI to GCS object name and downloads
5. **Delete**: Blob deletion removes objects from GCS bucket and cleans up metadata references

### Key Features

- **Direct Integration**: No factory patterns or abstractions - direct Google Cloud Storage client usage
- **Automatic Bucket Creation**: Creates GCS bucket automatically if it doesn't exist
- **URI Mapping**: Converts Episerver blob URIs to GCS object names seamlessly
- **Error Handling**: Graceful handling of missing objects and network issues
- **Metadata Support**: Preserves content type and file information