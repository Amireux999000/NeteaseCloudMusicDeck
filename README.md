# NetEase Cloud Music Pilot for Macro Deck

A [Macro Deck 3](https://macro-deck.app/) plugin that lets you control **NetEase Cloud Music (网易云音乐)** from your phone or tablet — play, pause, skip, seek, adjust volume, like songs, and see live now-playing info with album artwork.

**Author:** amireux999000  
**License:** MIT  
**Repository:** https://github.com/amireux999000/NeteaseCloudMusicDeck

Inspired by [SpicetifyDeck](https://github.com/Cjhackeryt/SpicetifyDeck). Instead of a modding framework bridge, this plugin uses **Windows System Media Transport Controls (SMTC)** — the same API Windows uses for the media overlay on your keyboard and lock screen. No API keys, no third-party mods, no extra login.

> This plugin is an unofficial, community-developed integration. It is not affiliated with, endorsed by, or sponsored by NetEase (网易) or Macro Deck.

## Features

### Playback control
- **Play / Pause / Toggle** — with button state reflecting current playback
- **Next / Previous track**
- **Restart track** — jump back to the beginning
- **Seek to** — jump to any percentage of the song
- **Set volume / Volume step / Mute** — via NetEase Cloud Music shortcuts

### App control
- **Toggle Like** — like/unlike the current song (Alt+L)
- **Toggle desktop lyrics** — show/hide lyrics (Ctrl+D)
- **Open / focus NetEase Cloud Music** — launch or bring to front

### Live deck values (variables)
| Variable | Description |
|---|---|
| `is_playing` | Whether music is currently playing |
| `playback_state` | Playing / Paused / Stopped |
| `current_position` | Current position as `m:ss` |
| `track_duration` | Total duration as `m:ss` |
| `progress_seconds` | Position in seconds (writable) |
| `progress_percentage` | Position as % (writable) |
| `track_name` | Current song title |
| `artist_name` | Current artist |
| `album_name` | Current album |
| `album_artist` | Album artist |
| `track_number` | Track number on album |
| `volume` | Volume level 0–100 (writable) |
| `shuffle` | Shuffle enabled |
| `repeat_mode` | Off / Track / Context |
| `connection_status` | Connected / Waiting |

### Music player widget
The plugin registers as a Macro Deck **Music Player**, so you can add a now-playing widget that shows album artwork, track title, artist, and a progress bar — with built-in transport controls.

## Requirements

- **Windows 10 1809 (build 17763) or later** (SMTC API requirement)
- **Macro Deck 3** (beta 15 or later)
- **NetEase Cloud Music** desktop client (UWP or Win32) running and playing music
- **.NET 10 Desktop Runtime** (installed automatically by Macro Deck if missing)

## Installation

1. Download the latest `.macroDeckPlugin` file from the [releases page](../../releases).
2. In Macro Deck, open **Plugins → Install from file** and select the downloaded file.
3. Restart Macro Deck if prompted.
4. Start NetEase Cloud Music and play a song.
5. The plugin will auto-detect the session — add actions or the music player widget to your deck.

> **Tip:** If the plugin shows "Waiting for NetEase Cloud Music," make sure the app is actually playing audio. SMTC only exposes sessions that have active media playback.

## How it works

```
┌─────────────────┐     SMTC events      ┌──────────────────────┐
│ NetEase Cloud    │ ◄──────────────────► │  SmtcMonitor         │
│ Music (playing)  │   play/pause/next    │  (BackgroundService) │
└─────────────────┘   media properties    └──────────┬───────────┘
                                                      │ snapshot
                                                      ▼
                                             ┌──────────────────────┐
                                             │ NeteaseStateManager   │
                                             │ (thread-safe state)   │
                                             └──────────┬───────────┘
                                                      │
                              ┌───────────────────────┼───────────────────────┐
                              ▼                       ▼                       ▼
                     ┌──────────────┐       ┌──────────────────┐    ┌─────────────────┐
                     │   Actions    │       │   Variables      │    │  Music Player   │
                     │ (13 actions) │       │  (15 variables)  │    │  (now playing)  │
                     └──────────────┘       └──────────────────┘    └─────────────────┘
```

- **SmtcMonitor** polls Windows SMTC every 1.5s and subscribes to session events. It identifies NetEase Cloud Music by its app user model ID (`cloudmusic`, `netease`, `163music`).
- Playback commands (play/pause/next/previous/seek) are sent directly through SMTC — reliable and fast.
- Volume, like, and lyrics use NetEase Cloud Music's global keyboard shortcuts (sent via `SendInput`).
- Album artwork is extracted from the SMTC thumbnail stream and cached.

## Building from source

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Macro Deck Plugin CLI: `dotnet tool install --global MacroDeck.Plugin.Cli --prerelease`

### Build
```bash
# Restore and build
dotnet build src/NeteaseCloudPilot/NeteaseCloudPilot.csproj -c Release

# Run with stub host (for development)
macrodeck-plugin run --project src/NeteaseCloudPilot --stub-host

# Package for win-x64
cd src/NeteaseCloudPilot
macrodeck-plugin build --output ../../artifacts
```

### Project structure
```
NeteaseCloudMusicDeck/
├── .github/workflows/release.yml   # Official release workflow
├── src/NeteaseCloudPilot/
│   ├── Program.cs                  # Plugin entry point & DI setup
│   ├── NeteaseIntegration.cs       # IPluginIntegration + IVariableProvider + IMusicPlayerProvider
│   ├── NeteaseMusicPlayer.cs       # IMusicPlayer implementation
│   ├── NeteaseValues.cs            # Constants & helpers
│   ├── Actions/                    # Amireux999000.NeteaseCloudPilot.Actions
│   ├── Media/                      # SmtcMonitor + WindowController
│   ├── State/                      # Snapshot + StateManager
│   ├── Variables/                  # Variable definitions & provider
│   ├── Localization/               # Strings.resx (中文) + Strings.en.resx (English)
│   ├── Assets/icon.svg
│   ├── manifest.json
│   └── macrodeck-build.json
├── LICENSE
└── README.md
```

## Limitations

- **Windows-only.** NetEase Cloud Music desktop is Windows-only and SMTC is a Windows API.
- **Shuffle/repeat** are read from SMTC but cannot be reliably changed (NetEase Cloud Music does not expose these through SMTC).
- **Volume** is controlled via keyboard shortcuts, not a direct API. Very large volume jumps may take a moment.
- **Like status** is not exposed by SMTC; the like action toggles but does not show current state.
- The plugin must be able to briefly focus the NetEase Cloud Music window to send keyboard shortcuts (like, volume, lyrics). This happens automatically and the previous foreground window is not permanently changed.

## Troubleshooting

**"NetEase Cloud Music is not running"**
- Start the NetEase Cloud Music app and play any song.
- If using the UWP (Store) version, make sure it's not suspended.
- Check that the app appears in Windows volume mixer (this confirms it has an audio session).

**Actions don't work**
- Ensure NetEase Cloud Music is the active SMTC session (pause other media apps).
- Some actions (like, volume) require the window to be focusable — minimize other fullscreen apps.

**Artwork doesn't show**
- NetEase Cloud Music must provide artwork through SMTC. Most versions do. If not, try updating the app.

## Privacy

This plugin does **not** collect, store, or transmit any personal data. All communication happens locally on your machine:
- Playback state is read from the Windows SMTC API (local).
- Commands are sent to the NetEase Cloud Music app via SMTC or keyboard shortcuts (local).
- No network requests are made to any external server.
- No credentials, tokens, or account information are accessed or stored.

## AI-Generated Assets

The plugin icon (`Assets/icon.svg`) was generated with AI assistance. It is an original design (a stylized cloud with a music note) and does not reproduce or copy any third-party logo or trademarked artwork.

## License

MIT — see [LICENSE](LICENSE) for the full text.
