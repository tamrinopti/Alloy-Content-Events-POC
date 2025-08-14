Upload Flow->

┌─────────────────┐    ┌──────────────────┐    ┌─────────────┐    ┌──────────────────┐
│   Optimizely    │    │  GcpBlobProvider │    │   GcpBlob   │    │ Google Cloud     │
│     CMS 12      │    │                  │    │             │    │    Storage       │
└─────────────────┘    └──────────────────┘    └─────────────┘    └──────────────────┘



 ┌────────────────────────────┐
 │   File Upload in CMS UI    │
 └─────────────┬──────────────┘
               │ HTTP POST
               ▼
    ┌────────────────────────────┐
    │ GcpBlobProvider.CreateBlob │
    └─────────────┬──────────────┘
                  │
                  ▼
       ┌──────────────────────┐
       │   Return GcpBlob     │
       └──────────┬───────────┘
                  │
                  ▼
    ┌────────────────────────────┐
    │ GcpBlob.OpenWrite()        │
    │ → returns GcpBlobWriteStream│
    └─────────────┬──────────────┘
                  │
                  ▼
     ┌──────────────────────────┐
     │ CMS writes bytes to      │
     │ GcpBlobWriteStream       │
     └─────────────┬────────────┘
                   │
                   ▼
    ┌────────────────────────────┐
    │ Stream Close triggers      │
    │ Upload to GCS via API      │
    └─────────────┬──────────────┘
                  │
                  ▼
     ┌─────────────────────────┐
     │ CMS stores blob URI in  │
     │ content database        │
     └─────────────────────────┘



Download Flow ->

┌─────────────────┐    ┌──────────────────┐    ┌─────────────┐    ┌──────────────────┐    ┌─────────────────┐
│   User/Browser  │    │  Optimizely CMS  │    │   GcpBlob   │    │ Google Cloud     │    │ GcpBlobFileInfo │
│                 │    │  Media Handler   │    │             │    │    Storage       │    │                 │
└─────────────────┘    └──────────────────┘    └─────────────┘    └──────────────────┘    └─────────────────┘



 ┌────────────────────────────┐
 │   CMS Media Request (UI)   │
 └─────────────┬──────────────┘
               │
               ▼
    ┌────────────────────────────┐
    │  GcpBlobProvider.GetBlob() │
    └─────────────┬──────────────┘
                  │
                  ▼
       ┌──────────────────────┐
       │  Create GcpBlob obj  │
       │  (bucket + object)   │
       └──────────┬───────────┘
                  │
                  ▼
    ┌────────────────────────────┐
    │  GcpBlob.OpenRead()        │
    │  - Calls GCS API           │
    │  - Downloads object stream │
    └─────────────┬──────────────┘
                  │
                  ▼
     ┌─────────────────────────┐
     │ CMS streams to browser  │
     │ or CMS editor preview   │
     └─────────────────────────┘
