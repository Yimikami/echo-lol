<p align="center"><img src="assets/brand/echo-emblem.png" width="88" alt="Echo" /></p>

# Echo

A lightweight League of Legends recorder for Windows. Keep your plays, edit them, and share them.

- Automatically capture kills, assists and deaths, or record the entire match.
- Game audio by default; desktop audio and microphone are optional.
- Browse, watch, trim and delete recordings.
- Export clips with your saved keyboard/mouse and APM overlay.
- Optionally record gameplay inputs, watch them directly on clips with a movable, resizable overlay.
- Save a manual moment with a configurable shortcut; default: **Ctrl+Shift+F8**.

## Download

[Windows development build](https://github.com/Yimikami/echo-lol/releases/tag/development). Extract the ZIP and run Echo.exe. Setup can be skipped.

## Build and run

Requires Windows 10/11 x64, .NET 10 SDK, a hardware H.264 encoder, OBS Studio 33 runtime files and FFmpeg on PATH. Running a built copy requires the .NET 10 Desktop Runtime. OBS itself does not need to be running.

```powershell
git clone https://github.com/Yimikami/echo-lol.git
cd echo-lol
pwsh ./scripts/build.ps1 -BundleRuntime -ObsDirectory "C:\Program Files\obs-studio"
./artifacts/app/Echo.exe
```

Open Echo and play League. Recording starts automatically; closing the library keeps the recorder running. Choose **Export** while watching a clip to save it with the overlay. Enable **Record gameplay inputs** in Highlights before a match to use input overlays; old videos cannot reconstruct inputs.

Fullscreen monitor capture can include everything visible on the game's monitor while League has focus. Window capture isolates the game window. See [capture details](docs/architecture.md).

## Development

Early development. Windows packages are published automatically from main; version tags create versioned releases. Hardware encoding reduces overhead, but zero FPS impact is not guaranteed. See [validation](docs/validation.md), [build architecture](docs/architecture.md) and [contributing](https://github.com/Yimikami/echo-lol/issues).

## License

GPL-3.0-or-later. Built with libobs, FFmpeg, WPF UI, Phosphor Icons and Manrope. [Third-party notices](THIRD_PARTY_NOTICES.md).

Echo isn't endorsed by Riot Games and doesn't reflect the views or opinions of Riot Games or anyone officially involved in producing or managing Riot Games properties. Riot Games and all associated properties are trademarks or registered trademarks of Riot Games, Inc.
