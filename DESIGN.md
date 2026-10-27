# Echo desktop design

Echo is a native Windows WPF tool. Its operating mode is **Operate**: find a local play, watch the lead-in, trim a useful range, and save a copy. The user-selected composition remains a compact clip list with a large player; this revision replaces incomplete controls and the unstructured settings form.

## Visual system

Graphite `#14191C` canvas, `#191F22` navigation, `#1C2327` editor surfaces, warm white `#F1F4F2`, secondary `#ACBAB6`, and mint `#9DE1C7`. Coral indicates deaths and recording state; blue identifies assists. Segoe UI uses the native Windows text stack. Fluent System Icons and WPF UI 4.3.0 provide one icon family, themed controls, popups, focus and keyboard states. No browser, blur layer or animated background is added to the recorder.

A 52px title bar and 166px navigation rail frame the workspace. The initial window is 1480×920, with a supported minimum of 1100×720. A 318px virtualized list sits beside a flexible player. Selected-clip actions are adjacent to the title. Playback volume, mute and fullscreen are directly beside the seek control. Fullscreen hides the library and editor while retaining the media element and playback controls.

## Timeline and editing

The timeline draws at most eight real preview frames, timestamps, event markers, a playhead and two draggable trim edges. Dragging an edge previews that boundary. In/out time fields and I/O shortcuts provide precise editing; reset restores the catalog range. Clicking an event starts playback three seconds before its recorded time, clamped to available footage and the selected trim range. Replay origins are included in this mapping.

Frame generation is serial, bounded, cached and deferred during active recording. The private thumbnail cache retains at most 320 images. Deletion uses a protected confirmation. Standalone media goes to the Windows Recycle Bin; removing a virtual highlight keeps its shared full-match source. Deleting a match removes its dependent highlights. A failed recycle operation restores the catalog.

## Settings and automatic capture

Recording, audio, highlights, quality and storage form clearly separated sections, with a persistent save footer and inline errors. The right rail explains automatic recording and playback shortcuts. Game audio is the default; desktop and microphone remain opt-ins. Settings explicitly describe monitor capture and next-recording application.

Opening the library starts the recorder if absent and resumes a healthy paused service. A native fault remains visible for an explicit retry rather than being hidden in an automatic crash loop. The library can close independently of capture.

## Research

- [Medal Quick Trimmer](https://medaltv.freshdesk.com/support/solutions/articles/48001170449-how-to-upload-your-medal-clips-on-desktop-app): clips open directly into trimming and audio workflows.
- [Screen Studio trimming](https://preview.screen.studio/guide/trimming): direct manipulation of clip edges and a visible timeline.
- [WPF UI](https://wpfui.lepo.co/documentation/getting-started.html): native Fluent controls, theme dictionaries and SymbolIcon.

These informed interaction choices; their source code, media and branding are not copied.

## Verification and limits

Two bounded offscreen WPF visual-tree render rounds covered 1480×920 and 1100×720 with real local clip metadata/posters. The first revealed an unapplied application theme; the corrected renders verified dark controls, readable inputs and a fitting editor. These are application render artifacts, not desktop screenshots or a live gameplay compatibility claim. Behavioral checks exercise volume/mute, fullscreen layout entry/exit, event lead-in and delete cancellation. Core tests exercise actual catalog/file operations via an injected recycle adapter. Live Windows fullscreen/taskbar behavior, media playback and the system Recycle Bin still benefit from normal-user acceptance.

The original emblem retains its generation provenance. Earlier image-generated concepts remain labeled as mockups. Private recordings, rendered previews and local profiles are not published with the source.
