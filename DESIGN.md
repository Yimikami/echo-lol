# Echo studio design

Echo's interface is a local highlight studio. The earlier mint list/player dashboard was replaced at the user's request. This redesign used the Anthropic frontend-design guidance, Vercel Web Interface Guidelines as applicable to native controls, and real Cap / DaVinci Resolve reference screens. Impeccable was not used for this revision.

## Visual system

- Canvas `#151922`, surfaces `#202733`, raised controls `#2A3444`, rules `#465269`.
- Text `#F6F8FC`, secondary `#BDC7D7`, ice-blue selection `#A9C9FF`. Secondary copy uses the shared brush rather than independently dimmed colors.
- Bundled Manrope at 400/500/600/700, with 30px page titles, 17–18px section headings, 14px medium-weight body and 12px metadata. Original variable font and OFL notice are retained; static instances avoid platform-dependent variable-font handling.
- One regular-weight Phosphor icon family. Exact upstream SVG paths are converted into native WPF geometries; there is no raster icon atlas, emoji icon or per-frame SVG decoder.
- Custom shared button, input, switch and slider styles with explicit hover/focus/disabled states. WPF UI supplies native themed dropdowns and related framework resources; its icon family is no longer used by Echo's interface.

## Library

A 76px navigation rail anchors Library, Matches and Settings. The main surface is a photographic contact sheet, with 16:9 previews, champion, event and duration. Search, event filters, newest/oldest sorting and pagination are real controls. Clips within a match sort by their recording origin and selected start, not only the shared session timestamp.

A page holds at most 32 items. Metadata is separate from thumbnails: images decode lazily and use weak caches so loading the library does not eagerly decode every saved clip. No decorative continuous animation runs.

## Editor

Opening a clip switches to a large player with a seek bar; Back / Escape returns to the gallery. Editing controls stay hidden until **Trim** is selected. Then the 250px inspector holds trim in/out, selection duration and MP4 export. The timeline owns the full bottom width and draws bounded real video frames with preserved aspect ratio, a time ruler, direct trim edges, playhead and Phosphor event icons. The inspector can scroll at the minimum height while keeping Export accessible. Kill, death and assist counts use sword, skull and handshake icons consistently across the library and timeline.

Space plays/pauses, F or double-click opens fullscreen, Esc exits fullscreen, M mutes, arrows seek, and I/O set the trim range. Event previews start three seconds before the action, clamped to available footage. Fullscreen keeps the same media element and hides navigation, inspector and timeline. Playback audio is independent of recording-source settings.

Selected poster images are generated at up to 1280px. Poster and filmstrip work is serial, one decoder thread per frame, cached, and deferred during active capture. Failed preview generation retains the existing thumbnail; it does not modify the source video.

## Settings and feedback

Recording, Audio, Highlights and Storage have dedicated categories. Rows align labels and controls; audio rows have explicit toggles. Selections save immediately, numeric edits save after a 450ms pause, and typed folder paths save on leaving the field. Folder-picker selections save immediately. Invalid edits stay visible without replacing valid settings. There is no Save changes button. The footer distinguishes saved settings from changes that apply next recording. Highlights settings include a configurable global manual shortcut and optional removal of focus-masked intervals from new highlights. Icon controls have automation names; trim has time-field alternatives and keyboard controls. Deletion has a focus-cycling confirmation and keeps the existing Recycle Bin/shared-source protections. Manual saves and deletions show visible, dismissible notifications even when the gallery is open. Redundant studio slogans and storage footnotes were removed.

## Research inspected

- Cap's actual editor image: https://cap.so/illustrations/app.webp
- Blackmagic Design Cut page and timeline image: https://www.blackmagicdesign.com/products/davinciresolve/cut
- Anthropic frontend-design: https://github.com/anthropics/skills/blob/main/skills/frontend-design/SKILL.md
- Vercel Web Interface Guidelines: https://github.com/vercel-labs/web-interface-guidelines
- Phosphor Core: https://github.com/phosphor-icons/core
- Google Fonts Manrope: https://github.com/google/fonts/tree/main/ofl/manrope

These informed layout and interaction; reference-product branding, media and proprietary source are not shipped.

## Verification

Native offscreen WPF renders covered gallery, normal player, optional trim editor and all four settings categories at 1480×920 and 1100×720. Forty-two checks passed, including embedded font/icon resolution, optional trimming, volume/mute, fullscreen restoration, event lead-in, confirmation cancellation, immediate audio/mode persistence, shortcut settings, active-match visibility, bounded pagination and search reset. Core tests: 53 passed. Global shortcut registration, conflict handling and release also passed a native Windows check.

These are actual application visual-tree renders and handler tests, not a claim of live gameplay FPS, Windows taskbar/fullscreen acceptance, full assist acceptance or native crash elimination. Private clip previews remain local. The public settings image is labeled as a render from the native UI.
