# Performance evidence

These are early development measurements from short, controlled fixtures on a Ryzen 5 5600 / RTX 4060 Ti system with OBS 33.0.0. They do not measure League FPS impact, and they are not a comparison against Outplayed, OBS Studio or another recorder.

| Workload | Duration | Average total CPU | Peak recorder working set | Notes |
|---|---:|---:|---:|---|
| Synthetic solid-color full recording | about 8 s | 0.52% | 141.9 MB | 1080p60 NVENC, unusually simple image |
| Synthetic event-triggered replay buffer | about 22 s | 0.97% | 156.8 MB | Three event saves, not gameplay |
| Moving window + isolated application audio | about 15 s | 1.36% | about 146 MB | Normal Windows access, 1080p60 NVENC |

CPU is process CPU time divided by wall time and logical processor count. Startup initialization and later remux/thumbnail work are outside these recording-window CPU averages. Working set is resident process memory; private committed memory is higher and must be reported separately in longer tests. These numbers exclude the UI, OS/driver allocations and hardware-encoder resource costs.

The moving-window native log reported two rendering-lag frames and two encoding-skipped frames, approximately 0.2%, even though the mux output's dropped-frame counter was zero. Dedicated lag counters are now retained in the test harness. No claim of dropped-frame-free recording is made.

## Gameplay measurement protocol

1. Keep game resolution, display mode, graphics settings and FPS limit fixed. Avoid simultaneous NVIDIA/Outplayed/OBS recording, or explicitly report it as part of the workload. Echo does not disable those applications automatically.
2. Warm up a training/custom game. Use the same camera, location and workload for samples.
3. Collect repeated baseline / clips / baseline / full-match samples with PresentMon, at least 30 seconds each. Keep the game foreground, particularly in fullscreen display mode.
4. Record average FPS, 1% lows, p95/p99 frame times, rendering/encoding lag, CPU, working set/private memory and relevant GPU encode/load readings.
5. Report run-to-run variation and scene differences. A single changing live-game scene cannot establish a causal percentage regression.

```powershell
./scripts/measure-gameplay.ps1 -PresentMonPath C:\Tools\PresentMon.exe -Label baseline -Seconds 30
./scripts/measure-gameplay.ps1 -PresentMonPath C:\Tools\PresentMon.exe -Label clips -Seconds 30
python ./scripts/analyze-frametimes.py ./artifacts/benchmarks/baseline-*.csv
```

The analysis script defines 1% low FPS as `1000 / mean(slowest 1% frame intervals in milliseconds)`. It uses the dominant swap chain and rejects invalid intervals. PresentMon collection is filtered to the League process, uses a unique ETW session, and does not collect input events. It does not inject into the game or read game memory. If Windows denies tracing, the script reports the failure instead of changing permissions.

## Design constraints

- One hardware encoding pipeline; full-match moments reference the original recording.
- Bounded compressed replay packets, no raw-video ring in managed memory.
- No preview renderer in the recorder host and no browser engine in the WPF UI.
- Game audio by default; desktop and microphone are opt-ins.
- UI timers stop when its process exits; exports do not compete with active recording.
- Fullscreen display masking checks foreground process immediately before each OBS frame composition. Display overlays and focus-transition behavior remain acceptance items.

Live gameplay measurements: **not yet available**.

The local PresentMon permission probe failed with Windows access denied. Collecting gameplay frame times requires the user to approve an elevated measurement process or use an account already permitted to trace performance events. No Windows group membership, security setting or game configuration was changed.
