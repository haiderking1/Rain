# Rain

A dark, minimal local music player for Windows, with a blurred (acrylic) window on Windows 11.

- Plays mp3, flac, m4a/aac, wav, wma and aiff from a folder you pick
- Cover art, shuffle, repeat, search, keyboard shortcuts
- Download songs by link (YouTube, SoundCloud, Bandcamp…) straight into your library, tagged with the cover embedded

## Build

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet run --project src/Rain
```

Downloading needs [yt-dlp](https://github.com/yt-dlp/yt-dlp), [ffmpeg](https://ffmpeg.org) and [deno](https://deno.com) on your PATH, e.g. `scoop install yt-dlp ffmpeg deno`.

## Shortcuts

| Key | Action |
| --- | --- |
| Space | Play / pause |
| Ctrl+← / Ctrl+→ | Previous / next |
| Ctrl+F | Search |
| Enter / double-click | Play selected track |

Built with WPF, [NAudio](https://github.com/naudio/NAudio) and [TagLib#](https://github.com/mono/taglib-sharp).
