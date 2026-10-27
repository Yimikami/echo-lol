# Validation ledger

This ledger separates implemented behavior, fixture evidence and live-game acceptance. A build pass is not a gameplay pass.

## Completed locally

- Core event/configuration/planning tests: **27 passed**. Covers identity aliases, event timestamps, multiple events per poll, replay deduplication, stale events, late-start baseline, clock reset, malformed data, configurable capture kinds, overlapping ranges, tail timing, force flush, long-fight segmentation and atomic sidecars.
- Native OBS 33.0.0 initialization and NVENC H.264 encoding at 1920×1080/60 FPS.
- Synthetic full recording: playable MP4 after validated MKV remux.
- Synthetic event sequence: kill, assist and death each produced a saved replay and catalog entry. These are fixtures, not Riot gameplay events.
- WGC capture of a moving test window: extracted video frame visually verified.
- Application audio isolation: the selected process played 440 Hz while another executable played 880 Hz. Recorded 440 Hz amplitude was approximately 2994.8; 880 Hz approximately 0.0017, about −124.7 dB relative. Both desktop and microphone options were off. This is a controlled fixture, not a universal isolation guarantee.
- Portable native-runtime smoke test: the assembled local package produced full video and replay saves using its copied OBS media components. A later smoke run also validated duration probing/remux with the single bundled FFmpeg executable.
- MP4 range-export engine exercised on a recorded fixture independently of the UI; full interactive export acceptance remains pending.
- WPF loaded the local catalog, real thumbnails, selected ranges and media duration. The first visual inspection found an unrendered paused first frame and standard bright slider/scrollbar styling; poster fallback and native control styling were subsequently implemented. The corrected UI still needs final interactive review.

## Important corrections discovered by testing

- Loading capture plugins before initializing D3D11 disabled their WGC capability detection. Initialization order was fixed.
- OBS helper executables must sit beside the recorder host, and their DLL dependencies must be discoverable. Runtime setup now stages these explicitly.
- A recording can be technically valid while its video is black if Windows capture access is denied. A restricted-context WGC run was rejected as capture evidence; a normal Windows-access run produced the verified moving frame. Missing-frame checks now surface this condition.
- Output dropped-frame counters do not describe rendering or encoding lag. Dedicated rendering/encoding counters were added after the first measurements.
- The recorder owns its log instead of depending on output pipes owned by the library UI.
- Desktop audio includes game sound, so the dedicated game source is disabled when the full desktop mix is explicitly selected.

## Pending acceptance

- Real League kill/assist/death events in a training/custom game.
- Real exclusive fullscreen capture and Alt+Tab masking; fullscreen display support has been implemented but must not yet be presented as validated compatibility/privacy.
- Paired PresentMon gameplay samples with identical settings and a comparable scene.
- Corrected UI at standard and smaller desktop sizes, playback, trim export and library-close survival.
- Windows CI and public repository publication.
- Long sessions, abrupt termination, low disk, audio-device changes, AMD/Intel GPUs and lower-end systems.

Update the ledger with actual evidence as checks complete; do not convert a pending row into a pass based on code inspection alone.
