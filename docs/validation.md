# Validation ledger

This ledger separates implemented behavior, fixture evidence and live-game acceptance. A build pass is not a gameplay pass.

## Completed locally

- Core event/configuration/planning tests: **66 passed**. Covers identity aliases, event timestamps, multiple events per poll, replay deduplication, stale events, late-start baseline, clock reset, malformed data, configurable capture kinds, overlapping ranges, tail timing, force flush, long-fight segmentation and atomic sidecars. Also covers three-second lead-ins, replay-origin and discontinuous segment mapping, trim bounds, shared recording deletion, recycling rollback, active/out-of-library protection, shortcut validation and actual-state mask intervals.
- Native OBS 33.0.0 initialization and NVENC H.264 encoding at 1920×1080/60 FPS.
- Synthetic full recording: playable MP4 after validated MKV remux.
- Synthetic event sequence: kill, assist and death each produced a saved replay and catalog entry. These are fixtures, not Riot gameplay events.
- WGC capture of a moving test window: extracted video frame visually verified.
- Application audio isolation: the selected process played 440 Hz while another executable played 880 Hz. Recorded 440 Hz amplitude was approximately 2994.8; 880 Hz approximately 0.0017, about −124.7 dB relative. Both desktop and microphone options were off. This is a controlled fixture, not a universal isolation guarantee.
- Portable native-runtime smoke test: the assembled local package produced full video and replay saves using its copied OBS media components. A later smoke run also validated duration probing/remux with the single bundled FFmpeg executable.
- MP4 range-export engine exercised on a recorded fixture independently of the UI; full interactive export acceptance remains pending.
- Live fullscreen practice session: **28 kill events, 1 death event and 15 saved clips** in a completed recording session of about 9 minutes. A saved frame was visually verified as actual League gameplay. Assist detection remains fixture-verified; this solo practice session did not establish live assist acceptance.
- Public repository published at https://github.com/Yimikami/echo-lol; Windows CI passed on the earlier published recording/library revisions. New fixes must be checked at their own commit.
- Studio redesign: a native gallery, dedicated editor and four settings categories rendered at 1480×920 and 1100×720. Manrope and regular Phosphor vectors resolve from bundled assets. These are offscreen application renders, not desktop screenshots or gameplay evidence.
- **46 UI checks passed** across both sizes: automatic startup, font/icon resources, gallery/player/back navigation, opt-in trimming, volume/mute, visible volume-track geometry, fullscreen layout restoration, event lead-in, delete confirmation/cancellation, settings categories/validation/persistence, immediate audio/mode saving, shortcut persistence, active full-match visibility, bounded pagination and search reset.
- Global shortcut registration, conflict detection and release/re-registration passed against real Windows APIs. No key injection was used; in-game shortcut acceptance remains open.
- Desktop loopback audio: the packaged recorder captured a deliberately quiet 997 Hz system-output fixture in a 7.07-second synthetic recording. Band-limited output measured −55.1 dB mean / −51.9 dB peak. This verifies this machine's current default output device, not every audio routing setup. Microphone was off.
- Automatic highlight preparation: an eight-second synthetic A/V recording with a masked interval from 2–4 seconds produced a decodable 5.88-second copy (including boundary guards). A nonzero-start 1–7-second highlight produced a decodable 3.88-second copy. Event mapping crossed the removed interval correctly; SHA-256 confirmed the original full-match file was unchanged. These are controlled media fixtures, not new live Alt+Tab acceptance.
- Public-source previews include only the native settings view; private gameplay previews, profiles and recordings remain excluded from Git.

- The optional Raw Input observer registered keyboard/mouse devices and removed both registrations in a packaged native check without key injection. Actual game input ingestion remains pending. Settings have no save-status label; input recording is opt-in.

## Important corrections discovered by testing

- Loading capture plugins before initializing D3D11 disabled their WGC capability detection. Initialization order was fixed.
- OBS helper executables must sit beside the recorder host, and their DLL dependencies must be discoverable. Runtime setup now stages these explicitly.
- A recording can be technically valid while its video is black if Windows capture access is denied. A restricted-context WGC run was rejected as capture evidence; a normal Windows-access run produced the verified moving frame. Missing-frame checks now surface this condition.
- Output dropped-frame counters do not describe rendering or encoding lag. Dedicated rendering/encoding counters were added after the first measurements.
- The recorder owns its log instead of depending on output pipes owned by the library UI.
- Live focus transitions exposed an over-eager no-video timeout and unreliable rectangle-based exclusive-fullscreen detection. Continuous reacquisition grace, largest-window selection, and explicit foreground-gated monitor capture replaced those assumptions.
- Windows reported a native `obs.dll` access violation after a later startup/cleanup attempt. The recorder now isolates each native recording lifecycle in a supervised worker; a native fault pauses capture, retains media and restores the control endpoint. This containment change still needs longer real-world use; it is not a claim that every underlying native crash is fixed.
- Desktop audio includes game sound, so the dedicated game source is disabled when the full desktop mix is explicitly selected.
- The user's missing desktop audio/full match selections had not been saved: persisted settings and recent sessions still used Clips mode with desktop audio off. Settings now save automatically; active and preparing full matches also appear in Matches. Old Clips-only sessions cannot reconstruct a full match.
- New sessions retain their recording-option snapshot and focus-mask timings. Post-game preparation creates separate cleaned highlight files and retains the full recording and MKV originals; old sessions without timing metadata are not guessed from black pixels.

## Pending acceptance

- Live assist events and further match transitions.
- Real exclusive fullscreen capture and Alt+Tab masking; fullscreen display support has been implemented but must not yet be presented as validated compatibility/privacy.
- Paired PresentMon gameplay samples were interrupted and then cancelled at the user's request. No FPS-regression percentage is established; the user will provide normal-use feedback.
- Live playback/fullscreen/taskbar behavior, interactive trim export, Windows Recycle Bin integration and library-close survival after the latest UI changes. Layout and control-handler checks have passed; they do not replace real desktop acceptance.
- Match GitHub Actions results to the exact revision being reviewed; earlier CI is not evidence for a later change.
- Long sessions, abrupt termination, low disk, audio-device changes, AMD/Intel GPUs and lower-end systems.

Update the ledger with actual evidence as checks complete; do not convert a pending row into a pass based on code inspection alone.

## Playback overlays and setup
- The custom-input/activity revision passed 75 core checks, 42 controlled overlay/setup checks and 50 offscreen UI checks at 1480px and 1100px widths. Checks cover decoded exports for four layouts, original-media preservation, per-key selection and mouse visibility, saved placement, complete HUD-preview visibility, activity navigation, event types and fullscreen controls.
- The settings preview prefers a locally cached full frame from the user's recording. Frame extraction is deferred during capture and the image remains in the local profile; the bundled Riot tutorial frame is only a fallback before recordings exist. A Lucian clip with minimap, skill bar and inventory was selected for this development profile.
- Playback keeps an activity/event timeline visible outside trim mode. Its bounded, cached chart shows a rolling 10-second input rate expressed as actions per minute; the overlay's existing APM counter remains a trailing-60-second press count. Cleaned video segments map back to the original input times. Missing input logs show no fabricated activity.
- The redesigned five-step setup retains Skip defaults, tests every step and validates clip timing before proceeding. UI/handler checks and the real-recording screenshots do not replace live desktop acceptance.
- Exit Echo was exercised through the same durable exit request used by the tray. An isolated paused UI/supervisor/worker group went from three processes to zero and did not respawn. No game recording or key injection was used.
- APM counts observed presses in the trailing 60 seconds. Old recordings show idle keys and an unavailable APM value. New input recording, chat suppression and Vanguard behavior still need live gameplay acceptance.
