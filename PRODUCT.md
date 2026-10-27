# Echo

## Platform
Windows desktop, native WPF. This is not a web or mobile product.

## Stack
Confirmed by the user: C#/.NET, WPF, libobs. Separate recorder and UI processes.

## Users and purpose
League of Legends players who want automatic kill, assist, and death clips, optional full-match recording, and a local clip library with playback and trimming. The primary requirement is low gameplay impact, stability, and little background clutter.

## Confirmed workflows
The first screen prioritizes the clip library and selected video's player. Two recording modes: clips only, or full match with event ranges. Overlapping moments can merge. All user-facing copy and repository documentation are English. Game audio is on by default; desktop and microphone audio are explicit opt-ins. No automatic fallback from game audio to desktop audio.

## Brand and design workflow
The user proposed Echo. Produce visual mockups before implementing the UI; the user selects a mockup. Name, original emblem, public GitHub repository, and coherent commits are required.

## Evidence and limits
No gameplay performance results exist yet. Synthetic/native tests must be labeled separately from live League acceptance. Never promise zero FPS loss. Existing OBS installation may be used for development but is not modified. Do not require OBS Studio's UI to run.

## Principles
- Keep capture and encoding in native libraries, without per-frame managed copies.
- The UI can exit while the recorder continues.
- Bounded memory, export queues, and storage; no cloud account or automatic upload.
- Errors must be visible and actionable; failed capture must not silently become desktop recording.
