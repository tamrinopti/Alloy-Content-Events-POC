# Optimizely CMS 12 - Google Cloud Storage Integration Flow

## Architecture Components

```
┌─────────────────┐    ┌──────────────────┐    ┌─────────────┐    ┌──────────────────┐
│   Optimizely    │    │  GcpBlobProvider │    │   GcpBlob   │    │ Google Cloud     │
│     CMS 12      │    │                  │    │             │    │    Storage       │
└─────────────────┘    └──────────────────┘    └─────────────┘    └──────────────────┘
```

## Upload Flow

```mermaid
sequenceDiagram
    participant CMS as Optimizely CMS 12
    participant Provider as GcpBlobProvider
    participant Blob as GcpBlob
    participant GCS as Google Cloud Storage
    
    CMS->>Provider: File Upload (HTTP POST)
    Provider->>Provider: CreateBlob()
    Provider->>Blob: Return GcpBlob instance
    Blob->>Blob: OpenWrite()
    Note over Blob: Returns GcpBlobWriteStream
    CMS->>Blob: Write bytes to stream
    Blob->>GCS: Stream Close triggers upload
    Note over GCS: Upload via GCS API
    CMS->>CMS: Store blob URI in database
```

### Step-by-Step Upload Process

1. **File Upload Initiation**
   ```
   ┌────────────────────────────┐
   │   File Upload in CMS UI    │
   └─────────────┬──────────────┘
                 │ HTTP POST
                 ▼
   ```

2. **Blob Creation**
   ```
   ┌────────────────────────────┐
   │ GcpBlobProvider.CreateBlob │
   └─────────────┬──────────────┘
                 │
                 ▼
   ```

3. **GcpBlob Instance**
   ```
   ┌──────────────────────┐
   │   Return GcpBlob     │
   └──────────┬───────────┘
              │
              ▼
   ```

4. **Write Stream Creation**
   ```
   ┌────────────────────────────┐
   │ GcpBlob.OpenWrite()        │
   │ → returns GcpBlobWriteStream│
   └─────────────┬──────────────┘
                 │
                 ▼
   ```

5. **Data Writing**
   ```
   ┌──────────────────────────┐
   │ CMS writes bytes to      │
   │ GcpBlobWriteStream       │
   └─────────────┬────────────┘
                 │
                 ▼
   ```

6. **Upload to GCS**
   ```
   ┌────────────────────────────┐
   │ Stream Close triggers      │
   │ Upload to GCS via API      │
   └─────────────┬──────────────┘
                 │
                 ▼
   ```

7. **Database Storage**
   ```
   ┌─────────────────────────┐
   │ CMS stores blob URI in  │
   │ content database        │
   └─────────────────────────┘
   ```

## Download Flow

```mermaid
sequenceDiagram
    participant Browser as User/Browser
    participant CMS as Optimizely CMS
    participant Provider as GcpBlobProvider
    participant Blob as GcpBlob
    participant GCS as Google Cloud Storage
    
    Browser->>CMS: Media Request (CMS UI)
    CMS->>Provider: GcpBlobProvider.GetBlob()
    Provider->>Blob: Create GcpBlob object
    Note over Blob: (bucket + object reference)
    Blob->>GCS: OpenRead() - GCS API call
    GCS->>Blob: Download object stream
    Blob->>Browser: CMS streams to browser/preview
```

### Architecture Overview

```
┌─────────────────┐    ┌──────────────────┐    ┌─────────────┐    ┌──────────────────┐    ┌─────────────────┐
│   User/Browser  │    │  Optimizely CMS  │    │   GcpBlob   │    │ Google Cloud     │    │ GcpBlobFileInfo │
│                 │    │  Media Handler   │    │             │    │    Storage       │    │                 │
└─────────────────┘    └──────────────────┘    └─────────────┘    └──────────────────┘    └─────────────────┘
```

### Step-by-Step Download Process

1. **Media Request**
   ```
   ┌────────────────────────────┐
   │   CMS Media Request (UI)   │
   └─────────────┬──────────────┘
                 │
                 ▼
   ```

2. **Blob Retrieval**
   ```
   ┌────────────────────────────┐
   │  GcpBlobProvider.GetBlob() │
   └─────────────┬──────────────┘
                 │
                 ▼
   ```

3. **Object Creation**
   ```
   ┌──────────────────────┐
   │  Create GcpBlob obj  │
   │  (bucket + object)   │
   └──────────┬───────────┘
              │
              ▼
   ```

4. **Stream Download**
   ```
   ┌────────────────────────────┐
   │  GcpBlob.OpenRead()        │
   │  - Calls GCS API           │
   │  - Downloads object stream │
   └─────────────┬──────────────┘
                 │
                 ▼
   ```

5. **Content Delivery**
   ```
   ┌─────────────────────────┐
   │ CMS streams to browser  │
   │ or CMS editor preview   │
   └─────────────────────────┘
   ```

## Key Features

- **Seamless Integration**: Direct integration between Optimizely CMS 12 and Google Cloud Storage
- **Stream Processing**: Efficient handling of large files through streaming
- **Scalable Storage**: Leverages Google Cloud Storage for unlimited scalability
- **Content Management**: Maintains blob URI references in CMS database for quick access