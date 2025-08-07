# 📦 GcsBlobProvider for Optimizely CMS 12

A custom **Blob Storage Provider** for storing and retrieving media assets (images, videos, PDFs, etc.) in **Google Cloud Storage (GCS)** for **Optimizely CMS 12**.

This provider integrates directly with the Optimizely CMS media pipeline and supports secure, signed URLs for controlled frontend delivery of files.

---

## 🚀 Features

- ✅ Full CMS editor support for media upload/download
- ✅ Stores all media files in a configurable GCS bucket
- ✅ Signed URL generation for secure access
- ✅ Plug-and-play with native `BlobProvider` architecture
- ✅ Compatible with CMS import/export and versioning tools

---

## 📥 Installation

Install via NuGet:

```bash
dotnet add package YourCompany.Optimizely.GcsBlobProvider
⚙️ Configuration
Add this section to your appsettings.json:

json
Copy
Edit
"GcsSettings": {
  "BucketName": "your-gcs-bucket-name",
  "ServiceAccountKeyPath": "path/to/service-account.json",
  "SignedUrlDurationMinutes": 60
}
🛠️ Register the Blob Provider
In Startup.cs or Program.cs:

csharp
Copy
Edit
public void ConfigureServices(IServiceCollection services)
{
    services.Configure<GcsSettings>(Configuration.GetSection("GcsSettings"));

    services.AddBlobProvider<GcsBlobProvider>("GcsProvider", options =>
    {
        options.Default = true; // Set GCS as the default blob storage provider
    });
}
🔄 How File Upload Works in Optimizely CMS
plaintext
Copy
Edit
[CMS Editor Upload] 
       ↓
[IBlobFactory.CreateBlob()] 
       ↓
[GcsBlobProvider.CreateBlob() returns GcsBlob]
       ↓
[CMS calls GcsBlob.OpenWrite()]
       ↓
[File is streamed into the blob stream]
       ↓
[Stream closed → triggers upload to GCS]
       ↓
[Blob reference stored in CMS DB]
CMS handles streaming and uploading automatically — your implementation simply needs to support OpenWrite() and OpenRead() in GcsBlob.

🔐 Signed URL Access
You can use GcsBlob.GetSignedUrl() to securely serve files on your website:

csharp
Copy
Edit
var blob = media.BinaryData as GcsBlob;
var signedUrl = blob?.GetSignedUrl();
Then in your Razor view:

html
Copy
Edit
<video controls>
  <source src="@Model.VideoUrl" type="video/mp4">
</video>
🧾 File Deletion
When media is deleted in the CMS, the corresponding file is also deleted from the GCS bucket by GcsBlobProvider.Delete().

✅ Requirements
Optimizely CMS 12+

.NET 6 or later

EPiServer.CMS.Core

Google.Cloud.Storage.V1

A Google Cloud project with a service account JSON key

📚 API Overview
Class	Description
GcsBlobProvider	Inherits from BlobProvider, handles file routing
GcsBlob	Inherits from Blob, streams data to/from GCS

🧪 Testing
Upload images or videos via the CMS Assets panel.

Use the media file in content blocks or views.

Delete content and verify the corresponding GCS file is removed.