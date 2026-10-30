# Khylo Platform: Client-Independent API Contract & Handoff Specification
## Phase 2 Client Applications Engineering Manual
### For Android, Roku, Apple TV, iOS, Web, and Desktop Engineering Teams

---

## 1. Introduction & Architecture Overview

The **Khylo Server** exposes a unified, client-agnostic REST and WebSocket API capable of powering any consumer frontend. Client applications (Android, Roku, iOS, Smart TVs, Web players) interact with the server as an autonomous API client without needing insight into server-side storage topologies, database schemas, or transcoding pipelines.

### Standard Endpoints & Documentation
- **OpenAPI 3.1 JSON Specification**: `http://<server>:8096/api-docs/openapi.json`
- **Interactive Swagger UI**: `http://<server>:8096/api-docs/swagger`
- **Interactive ReDoc Documentation**: `http://<server>:8096/api-docs/redoc`

---

## 2. Authentication & Session Management

All requests (except public endpoints like invitations redemption and server discovery) require authentication.

### Authentication Headers
Supply the API token obtained during authentication via either header:
```http
X-Emby-Token: <access_token>
```
or
```http
Authorization: MediaBrowser Token="<access_token>"
```

### Required Client Identity Header
Clients must identify themselves by providing the `X-Emby-Authorization` header:
```http
X-Emby-Authorization: MediaBrowser Client="Khylo Android", Device="Pixel 9 Pro", DeviceId="unique-device-uuid", Version="1.0.0"
```

### Authenticating a User
```http
POST /Users/AuthenticateByName
Content-Type: application/json

{
  "Username": "johndoe",
  "Pw": "UserPassword123!"
}
```
**Response (200 OK)**:
```json
{
  "User": {
    "Id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "Name": "johndoe",
    "Configuration": {
      "EnableNextEpisodeAutoPlay": true,
      "SkipIntroMode": "ShowButton",
      "SkipCreditsMode": "AutoSkip",
      "EnableInactivityProtection": true,
      "InactivityTimeoutMinutes": 120
    }
  },
  "AccessToken": "e89f81a7b45c43d7890abcdef1234567",
  "ServerId": "khylo-server-uuid"
}
```

---

## 3. Omni-Media Model & Endpoints

Khylo natively supports 8 distinct media families. Client applications query items using `GET /Items` or dedicated type routes.

### Item Types (`includeItemTypes`)
- `Movie`
- `Series`, `Season`, `Episode`
- `Audio` (Music Track), `MusicAlbum`, `MusicArtist`
- `AudioBook` (Spoken word audiobook with narrator, chapters, and separate progress)
- `Podcast`, `PodcastEpisode` (RSS feed channels and episodes)
- `Book` (EPUB/PDF ebooks with page count and progress)
- `Comic`, `ComicSeries` (CBZ/CBR archives with page counts)
- `Photo`, `PhotoAlbum` (Camera RAW/JPEG with EXIF telemetry)
- `Game`, `GamePlatform` (Classic console ROMs with CRC32 hashes)

### Querying Media Libraries
```http
GET /Items?userId={userId}&includeItemTypes=AudioBook&recursive=true
```
```http
GET /Items?userId={userId}&includeItemTypes=Comic&recursive=true
```
```http
GET /Items?userId={userId}&includeItemTypes=Game&recursive=true
```

---

## 4. Intro & Credit Skip Markers

Clients implement smart skip overlays (e.g. "Skip Intro", "Next Episode") by querying media segments for the current item.

### Querying Segments
```http
GET /MediaSegments/{itemId}
```
**Response (200 OK)**:
```json
[
  {
    "Id": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
    "ItemId": "d3b07384-d113-40e9-a352-7fd8df68341e",
    "Type": "Intro",
    "StartTicks": 850000000,
    "EndTicks": 1750000000
  },
  {
    "Id": "8c2deb4d-4c8e-5bfe-aced-3c1e8c4eda7e",
    "ItemId": "d3b07384-d113-40e9-a352-7fd8df68341e",
    "Type": "Outro",
    "StartTicks": 12800000000,
    "EndTicks": 13200000000
  }
]
```
*(Note: 10,000 ticks = 1 millisecond. 10,000,000 ticks = 1 second).*

### Client Behavior Rules
- If `User.Configuration.SkipIntroMode == "AutoSkip"`, the client player should automatically seek to `EndTicks` when the playhead enters `[StartTicks, EndTicks]`.
- If `SkipIntroMode == "ShowButton"`, render an interactive "Skip Intro" button while the playhead is between `StartTicks` and `EndTicks`.

---

## 5. Decoupled Playlist Progress

Khylo enforces strict separation between a user's normal "Continue Watching" item state and their playlist playback progress.

### Getting Playlist Playback State
```http
GET /Playlists/{playlistId}/PlaybackState?userId={userId}
```
**Response (200 OK)**:
```json
{
  "PlaylistId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "UserId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "CurrentItemIndex": 4,
  "CurrentItemPositionTicks": 15420000000,
  "IsShuffled": false,
  "LastPlayedDate": "2026-10-01T12:00:00Z"
}
```

### Updating Playlist Progress
```http
POST /Playlists/{playlistId}/PlaybackState?userId={userId}
Content-Type: application/json

{
  "CurrentItemIndex": 5,
  "CurrentItemPositionTicks": 0,
  "IsShuffled": false
}
```

---

## 6. Passout & Inactivity Protection

Khylo protects server resources and user battery life by monitoring continuous playback:
- If a client continues playing without any user interaction (key press, touch, seek) for longer than `User.Configuration.InactivityTimeoutMinutes` (default: 120 minutes), the server sends a remote `PlaystateCommand.Pause`.
- The client player must honor this command, halt audio/video rendering, and display an inactivity prompt (e.g., *"Are you still watching?"*).
- When the user taps *"Resume"*, send an unpause command to continue.

---

## 7. User Self-Hosted Invitations

Clients or onboarding portals can redeem server invitations directly without requiring pre-existing accounts:

### Inspect Invitation Details (Public)
```http
GET /Invitations/{code}/Details
```
**Response (200 OK)**:
```json
{
  "Code": "khylo-9a1b2c3d4e5f",
  "ServerName": "Khylo",
  "IsValid": true,
  "ExpiresAtUtc": "2026-10-03T12:00:00Z"
}
```

### Redeem Invitation (Public)
```http
POST /Invitations/{code}/Redeem
Content-Type: application/json

{
  "Username": "newuser",
  "Password": "SecurePassword123!"
}
```
**Response (200 OK)**:
```json
{
  "UserId": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
  "Username": "newuser"
}
```

---

## 8. Summary of Server Guarantees for Clients

1. **Hardware Acceleration**: 100% unlocked on the server side; clients can request transcode bitrates or direct-play without license gates.
2. **Deterministic Search**: `GET /Search/Hints` provides rich metadata across all 8 media families.
3. **Real-Time WebSockets**: Connect to `ws://<server>:8096/socket` to receive real-time server events, transcode notifications, and remote control commands.
