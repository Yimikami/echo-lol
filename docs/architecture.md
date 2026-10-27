# Architecture decision 0001: local recording first

Status: accepted by the user, 2026-10-27.

Echo uses a native WPF UI and a separate C# recorder host around libobs. The UI sends low-volume control messages; pixels never cross IPC or the managed heap. Recording, event detection, and session persistence survive closing the UI.

The first backend uses a narrowly scoped, original P/Invoke surface over the documented libobs C API. No code is copied from unlicensed community wrappers. Native runtime versions are validated and pinned in development setup. This project is GPL-3.0-or-later and preserves third-party notices.

Default capture is a selected League window using Windows Graphics Capture, with application audio from the same executable. Desktop and microphone capture are opt-ins. Display capture is never an automatic video fallback. Exclusive fullscreen and minimized windows must be tested and reported honestly; borderless is the initial supported target.

Clips-only mode uses one hardware encoder and an encoded replay buffer. Events are deduplicated by match epoch and event ID, scheduled using monotonic capture time, then saved after a configurable tail. Full-match mode records once and retains virtual clip ranges for later export. Exports run serially and are deferred while a game is being recorded.

Use a recoverable MKV container for full sessions. Replay saves and exports are validated before publication. Use atomic JSON sidecars to retain sessions and event ranges independently of the UI. Start with this portable catalog; add SQLite only when catalog scale warrants it.

Native capture tests, event tests, output checks, and process resource measurements are separate from gameplay benchmarks. Live FPS claims require paired runs under the same League workload and settings, including frame-time percentiles and 1% lows.

The WPF library uses bounded pages and lazy thumbnails. Normal playback includes an input-activity chart and event navigation; trimming opens separately. Settings save automatically. Shared frozen vector artwork supplies both keyboard/mouse playback overlays and exports, including custom key selections. Recording-derived placement previews stay in the local profile. First-run setup can be skipped, and themes follow shared resources.
