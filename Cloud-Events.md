# CMS Events to GCP Pub/Sub - POC Integration

Publishes **CMS content events** to Google Cloud Pub/Sub:

---


### Configuration**
```json
{
  "EventPublishing": {
    "Enabled": true,
    "UseConsolePublisher": true,
    "GcpProjectId": "your-project-id",
    "PubSubTopicName": "cms",
    "SourceUrl": "https://your-cms.com"
  }
}
```


### GCP
- Change `UseConsolePublisher: false`
- Add `GOOGLE_APPLICATION_CREDENTIALS` environment variable

---

## **Event Format**

```json
{
  "specversion": "1.0.1",
  "type": "com.youversionapi.cms.content.published.v1",
  "source": "https://your-cms.com",
  "subject": "123_456",
  "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "time": "2025-01-15T10:30:00Z",
  "datacontenttype": "application/json",
  "data": {
    "contentLink": "123_456",
    "contentGuid": "f1e2d3c4-b5a6-9876-5432-109876543210",
    "name": "My Article Page",
    "contentType": "ArticlePage",
    "language": "en",
    "url": "/articles/my-article-page",
    "modifiedDate": "2025-01-15T10:29:45Z",
    "modifiedBy": "admin",
    "contentId": 123
  }
}
```

---

## **Project Structure**

```
📁 Your.CMS.Project/
├── 📁 Models/
│   └── 📄 CloudEvent.cs                
├── 📁 Services/
│   └── 📄 EventPublisher.cs          
└── 📄 ContentEventsModule.cs            
```
