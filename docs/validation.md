# Validation ledger

This ledger separates implemented behavior, fixture evidence and live-game acceptance. A build pass is not a gameplay pass.

## Completed locally

- Core event/configuration/planning tests: **37 passed**. Covers identity aliases, event timestamps, multiple events per poll, replay deduplication, stale events, late-start baseline, clock reset, malformed data, configurable capture kinds, overlapping ranges, tail timing, force flush, long-fight segmentation and atomic sidecars. New cases cover three-second event lead-ins, replay-origin mapping, trim bounds, virtual/shared recording deletion, standalone recycling, rollback on recycle failure, and active/out-of-library protection.
- Native OBS 33.0.0 initialization and NVENC H.264 encoding at 1920×1080/60 FPS.
- Synthetic full recording: playable MP4 after validated MKV remux.
- Synthetic event sequence: kill, assist and death each produced a saved replay and catalog entry. These are fixtures, not Riot gameplay events.
- WGC capture of a moving test window: extracted video frame visually verified.
- Application audio isolation: the selected process played 440 Hz while another executable played 880 Hz. Recorded 440 Hz amplitude was approximately 2994.8; 880 Hz approximately 0.0017, about −124.7 dB relative. Both desktop and microphone options were off. This is a controlled fixture, not a universal isolation guarantee.
- Portable native-runtime smoke test: the assembled local package produced full video and replay saves using its copied OBS media components. A later smoke run also validated duration probing/remux with the single bundled FFmpeg executable.
- MP4 range-export engine exercised on a recorded fixture independently of the UI; full interactive export acceptance remains pending.
- Live fullscreen practice session: **28 kill events, 1 death event and 15 saved clips** in a completed recording session of about 9 minutes. A saved frame was visually verified as actual League gameplay. Assist detection remains fixture-verified; this solo practice session did not establish live assist acceptance.
- Public repository published at https://github.com/Yimikami/echo-lol; Windows CI passed on the earlier published recording/library revisions. New fixes must be checked at their own commit.
- WPF UI 4.3.0 and Fluent System Icons now theme native controls, including dropdowns and search. Two bounded offscreen visual-tree render rounds covered 1480×920 and 1100×720 with real local metadata/posters and a bounded filmstrip; the second confirmed corrected dark themes. These renders do not control or screenshot the desktop.
- Eleven normal-user-context behavior checks passed: automatic resume without Record, preserving an actionable native fault, catalog loading, playback volume/mute, fullscreen layout entry/exit, event lead-in, and delete confirmation/cancellation. Four additional layout assertions passed in the earlier render run. The same pipe check in the restricted execution context was rejected by OS pipe access; its normal-user-context run passed. This distinction is retained rather than treating the restricted run as product evidence.

## Important corrections discovered by testing

- Loading capture plugins before initializing D3D11 disabled their WGC capability detection. Initialization order was fixed.
- OBS helper executables must sit beside the recorder host, and their DLL dependencies must be discoverable. Runtime setup now stages these explicitly.
- A recording can be technically valid while its video is black if Windows capture access is denied. A restricted-context WGC run was rejected as capture evidence; a normal Windows-access run produced the verified moving frame. Missing-frame checks now surface this condition.
- Output dropped-frame counters do not describe rendering or encoding lag. Dedicated rendering/encoding counters were added after the first measurements.
- The recorder owns its log instead of depending on output pipes owned by the library UI.
- Live focus transitions exposed an over-eager no-video timeout and unreliable rectangle-based exclusive-fullscreen detection. Continuous reacquisition grace, largest-window selection, and explicit foreground-gated monitor capture replaced those assumptions.
- Windows reported a native `obs.dll` access violation after a later startup/cleanup attempt. The recorder now isolates each native recording lifecycle in a supervised worker; a native fault pauses capture, retains media and restores the control endpoint. This containment change still needs longer real-world use; it is not a claim that every underlying native crash is fixed.
- Desktop audio includes game sound, so the dedicated game source is disabled when the full desktop mix is explicitly selected.

## Pending acceptance

- Live assist events and further match transitions.
- Real exclusive fullscreen capture and Alt+Tab masking; fullscreen display support has been implemented but must not yet be presented as validated compatibility/privacy.
- Paired PresentMon gameplay samples were interrupted and then cancelled at the user's request. No FPS-regression percentage is established; the user will provide normal-use feedback.
- Live playback/fullscreen/taskbar behavior, interactive trim export, Windows Recycle Bin integration and library-close survival after the latest UI changes. Layout and control-handler checks have passed; they do not replace real desktop acceptance.
- Final Windows CI on the latest containment fixes.
- Long sessions, abrupt termination, low disk, audio-device changes, AMD/Intel GPUs and lower-end systems.

Update the ledger with actual evidence as checks complete; do not convert a pending row into a pass based on code inspection alone.
