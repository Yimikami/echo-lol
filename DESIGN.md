# Echo studio design

Echo's interface is a local highlight studio. The earlier mint list/player dashboard was replaced at the user's request. This redesign used the Anthropic frontend-design guidance, Vercel Web Interface Guidelines as applicable to native controls, and real Cap / DaVinci Resolve reference screens. Impeccable was not used for this revision.

## Visual system

- Canvas `#111214`, surfaces `#1A1C20`, raised controls `#24272D`, rules `#30333B`.
- Text `#F5F6F7`, secondary `#A1A7B2`, ice-blue selection `#C2D4FF`.
- Bundled Manrope at 400/600/700, with 30px page titles, 17–18px section headings, 14px body and 11–12px metadata. Original variable font and OFL notice are retained; static instances avoid platform-dependent variable-font handling.
- One regular-weight Phosphor icon family. Exact upstream SVG paths are converted into native WPF geometries; there is no raster icon atlas, emoji icon or per-frame SVG decoder.
- Custom shared button, input, switch and slider styles with explicit hover/focus/disabled states. WPF UI supplies native themed dropdowns and related framework resources; its icon family is no longer used by Echo's interface.

## Library

A 76px navigation rail anchors Library, Matches and Settings. The main surface is a photographic contact sheet, with 16:9 previews, champion, event and duration. Search, event filters, newest/oldest sorting and pagination are real controls. Clips within a match sort by their recording origin and selected start, not only the shared session timestamp.

A page holds at most 32 items. Metadata is separate from thumbnails: images decode lazily and use weak caches so loading the library does not eagerly decode every saved clip. No decorative continuous animation runs.

## Editor

Opening a clip switches to a dedicated editing workspace; Back / Escape returns to the gallery. A large player occupies the center. The 250px inspector holds trim in/out, selection duration and MP4 export. The timeline owns the full bottom width rather than sitting inside a small card. It draws bounded real video frames with preserved aspect ratio, a time ruler, direct trim edges, playhead and labeled event markers. The inspector can scroll at the minimum height while keeping Export accessible.

Space plays/pauses, F or double-click opens fullscreen, Esc exits fullscreen, M mutes, arrows seek, and I/O set the trim range. Event previews start three seconds before the action, clamped to available footage. Fullscreen keeps the same media element and hides navigation, inspector and timeline. Playback audio is independent of recording-source settings.

Selected poster images are generated at up to 1280px. Poster and filmstrip work is serial, one decoder thread per frame, cached, and deferred during active capture. Failed preview generation retains the existing thumbnail; it does not modify the source video.

## Settings and feedback

Recording, Audio, Highlights and Storage have dedicated categories. Rows align labels and controls; audio rows have explicit toggles. Fields retain an unsaved draft while moving between app destinations; Save changes writes only validated settings to the profile. Errors remain visible in the save footer. Icon controls have automation names; trim has time-field alternatives and keyboard controls. Deletion has a focus-cycling confirmation and keeps the existing Recycle Bin/shared-source protections. Manual saves and deletions show visible, dismissible notifications even when the gallery is open.

## Research inspected

- Cap's actual editor image: https://cap.so/illustrations/app.webp
- Blackmagic Design Cut page and timeline image: https://www.blackmagicdesign.com/products/davinciresolve/cut
- Anthropic frontend-design: https://github.com/anthropics/skills/blob/main/skills/frontend-design/SKILL.md
- Vercel Web Interface Guidelines: https://github.com/vercel-labs/web-interface-guidelines
- Phosphor Core: https://github.com/phosphor-icons/core
- Google Fonts Manrope: https://github.com/google/fonts/tree/main/ofl/manrope

These informed layout and interaction; reference-product branding, media and proprietary source are not shipped.

## Verification

Native offscreen WPF renders covered gallery, editor and all four settings categories at 1480×920 and 1100×720. Thirty-four checks passed, including embedded font/icon resolution, gallery/editor navigation, volume/mute, visible slider-track geometry, fullscreen layout restoration, three-second event lead-in, confirmation cancellation, settings validation/persistence, bounded pagination and search reset. The final packaged build passed the 20-check behavior subset and all 37 core tests.

These are actual application visual-tree renders and handler tests, not a claim of live gameplay FPS, Windows taskbar/fullscreen acceptance, full assist acceptance or native crash elimination. Private clip previews remain local. The public settings image is labeled as a render from the native UI.
