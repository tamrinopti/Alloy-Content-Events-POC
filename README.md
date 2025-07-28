# Optimizely CMS 12 Content Events Demo

A demonstration project showcasing content event logging in Optimizely CMS 12.

## Features

- **Content Events Logging**: Comprehensive logging of all content lifecycle events (Create, Save, Publish, Delete, Move, etc.)
- **API Endpoint**: REST API to update StartPage names and trigger content events
- **Event Monitoring**: Real-time logging of content operations with user information

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

### Update StartPage Name
**Endpoint**: `PUT /api/StartPageAPI/{contentId}/name`

**Example**:
```bash
PUT /api/StartPageAPI/5/name
Content-Type: application/json

{
  "pageName": "My Updated Page Name"
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
4. Use the `PUT /api/StartPageAPI/5/name` endpoint
5. Update with any text (e.g., "Test Page Update")
6. Check logs to see content events being triggered

## Log Output Example

```
[CONTENT EVENT] Publishing - Type: StartPage, Name: 'Test Page Update', ID: 5, User: admin
[CONTENT EVENT] Published - Type: StartPage, Name: 'Test Page Update', ID: 5, User: admin
[START PAGE EVENT] Published - StartPage specific data logged for 'Test Page Update'
```

## Requirements

- Optimizely CMS 12
- .NET 6+
- Valid StartPage content with ID 5 (or adjust the test ID accordingly)