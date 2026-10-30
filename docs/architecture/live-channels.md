# Khylo Live Channels

Always-on, themed "TV channels" built from the user's own movie and TV libraries. A channel is
only a schedule: nothing plays, is generated or transcodes until someone tunes in.

## How it works

| Piece | Where | Role |
|---|---|---|
| `LiveChannelManager` | `Khylo.Server.Implementations/LiveChannels` | Channel definitions, library snapshot, schedules, guide and tune-in |
| `LiveChannelScheduler` | same | Pure, deterministic schedule builder (unit tested) |
| `LiveChannelViewingRegistry` | same | Marks channel playback per user + device so it never touches watch progress |
| `LiveChannelsController` | `Khylo.Api/Controllers` | `/LiveChannels/...` REST API |
| Web guide and player | `khylo-web/src/components/khylo/live` | `/live` guide page, tune-in, channel up/down in the player |

### Schedules

- Definitions live in `config/khylo_live_channels.json`. Built-in channels (`LiveChannelCatalog`)
  have stable ids derived from their key and always follow the current catalog; user-created
  channels are kept as they are.
- Content comes from an in-memory snapshot of all movie/TV libraries, rebuilt every 30 minutes
  or shortly after library changes. Channels filter it by each title's own metadata: genres,
  first-air year (series) or release year (movies), series status/end date, official rating,
  tags and libraries. Titles without genres can't be classified and stay off channels.
- A channel airs TV series or movies, never both. Animation only airs on animation/kids/family
  channels; live-action channels exclude it.
- Each channel's schedule is persisted in `data/khylo/live-channels/{id}.json` and laid out
  48 hours ahead (extended again when less than 24 hours remain). Programs run back to back.
- Rotation: every movie/series in a channel airs once per shuffled "deck" before anything
  repeats; decks are seeded by channel id + deck number, so schedules are deterministic.
- Series air broadcast style, in blocks: a season of up to 13 episodes airs start to finish;
  longer seasons are split into equal parts (22 episodes → two halves of 11). A block never runs
  past 6 hours. Then the channel moves on to another series. Episodes air in order (no
  specials), and each series continues where it left off.
- Lineup: TV channels from 1, movie channels from 50, seasonal from 90, network channels from 200 (one per
  recognised TV network/streamer in the library's series metadata) and collection channels from
  400 (one per box set). The network and collection channels are generated from the library.
- Category channels (50 built-in: 23 TV, 24 movie, 3 seasonal) don't overlap: each title airs on
  exactly one of them — the first, by channel number, whose rules it matches. They are listed most
  specific first (animation, unscripted, themes like Superheroes, then broad genres). A category
  channel without enough content stays off the air and its titles move to their next match.
  Network, collection and seasonal channels draw from the whole library on top of that.
- Matching uses genres (TMDB/TVDB/IMDb names), TMDB keywords (stored as tags) via `AnyOf`, years,
  ratings and networks. Theme channels (keywords) exclude documentaries; seasonal channels
  (`ActiveMonths`) are only on the air in their months. Duplicate copies of a movie air once.
- Network channels come from `LiveChannelNetworks` (name + aliases + whether the network airs
  animation); their logos are fetched on demand from TMDB (`ILiveChannelLogoProvider`), cached in
  `cache/khylo/live-channel-logos` and served at `/LiveChannels/{id}/Logo`. Nothing is bundled.
- Variety: a channel goes on the air only with enough content — 4+ series or 10+ movies and 12+
  hours (collections: 4+ movies, 6+ hours) — so it doesn't loop the same few titles.
- Changing a channel's criteria starts a fresh schedule.

### Tuning in

`POST /LiveChannels/{id}/Tune` returns the current program, the offset into it, and the next
12 programs. The client plays those library items normally (direct play when possible) from
that offset. Leaving the queue's end re-tunes the channel live.

While on a channel the web player only allows pause and rewind
(`components/playback/navigationPolicy.ts`): seeking is capped at the live point, and next/previous
program, chapter skips, Skip Intro/Credits and the Up Next prompt are disabled.

The tune call registers those items for the user's device in `LiveChannelViewingRegistry`;
`SessionManager` skips user-data updates (resume position, played state, play count) for them,
so channel viewing never changes Continue Watching or Next Up. `POST /LiveChannels/Leave`
releases the device after a short grace period for the final stop report.

### Turning channels on and off

Admins manage this at Dashboard → Live Channels (`/dashboard/livechannels`). Settings live in
`config/khylo_live_channels_settings.json`:

- `Enabled` is the master switch. While it is off the guide is empty, tuning returns 404, no
  schedules are built, and the web client hides the sidebar link (it reads `/LiveChannels/Status`).
- `DisabledChannels` lists channel keys, so network and collection channels (which are generated
  from the library) stay off across restarts and lineup rebuilds. A turned-off category channel
  doesn't claim titles, so they move to the next category channel that matches them.

### Access

A user sees a channel only if they can access every library its content comes from and its
highest content rating is within their parental limit.

## API

| Method | Path | Notes |
|---|---|---|
| GET | `/LiveChannels/Guide?startTime=&hours=` | Channels with programs in the window (max 48h) |
| GET | `/LiveChannels/{id}?hours=` | One channel: upcoming schedule and the titles it rotates through |
| POST | `/LiveChannels/{id}/Tune` | What to play now and next |
| POST | `/LiveChannels/Leave` | Device stopped watching channels |
| GET | `/LiveChannels/Status` | Whether Live Channels is turned on |
| GET | `/LiveChannels/Admin` | Admin: master switch, every channel's state and content |
| POST | `/LiveChannels/Admin/Enabled` | Admin: turn Live Channels on/off (`{ Enabled }`) |
| POST | `/LiveChannels/Admin/Channels` | Admin: turn channels on/off (`{ ChannelIds, Enabled }`) |
| GET | `/LiveChannels/Definitions` | Admin: all definitions |
| POST | `/LiveChannels/Reset` | Admin: rebuild all schedules |

## Planned

- Admin: rename, renumber and edit channels; user-created custom channels.
- Resource-aware limits (e.g. cap concurrent channel transcodes).
- Watch together on a channel via SyncPlay.
- Optional bridge into Khylo Live TV so existing third-party apps can show the channels.
