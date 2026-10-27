"""Summarize an explicitly captured PresentMon CSV; never read game memory."""
import csv
import json
import math
import statistics
import sys
from pathlib import Path

def percentile(values, percent):
    index = (len(values) - 1) * percent / 100
    lo = math.floor(index)
    hi = math.ceil(index)
    return values[lo] + (values[hi] - values[lo]) * (index - lo)

def analyze(path):
    with open(path, encoding="utf-8-sig", newline="") as file:
        rows = list(csv.DictReader(file))
    if not rows:
        raise ValueError("No frames captured")
    key = next((k for k in ("FrameTime", "MsBetweenPresents", "msBetweenPresents") if k in rows[0]), None)
    if key is None:
        raise ValueError("Unsupported PresentMon columns: " + ", ".join(rows[0]))
    # Analyze the dominant swap chain; do not mix menus/overlays with gameplay.
    chains = {}
    for row in rows:
        chain = row.get("SwapChainAddress", "main")
        try:
            value = float(row[key])
            if math.isfinite(value) and value > 0:
                chains.setdefault(chain, []).append(value)
        except (ValueError, TypeError):
            continue
    if not chains:
        raise ValueError("No valid frame intervals")
    values = sorted(max(chains.values(), key=len))
    worst = values[-max(1, math.ceil(len(values) * 0.01)):]
    return {"file": Path(path).name, "frames": len(values), "average_fps": 1000 / statistics.mean(values),
            "observed_frame_time_seconds": sum(values) / 1000,
            "one_percent_low_fps": 1000 / statistics.mean(worst), "p95_frame_ms": percentile(values, 95),
            "p99_frame_ms": percentile(values, 99), "definition": "1% low = 1000 / mean of slowest 1% frame intervals"}

if __name__ == "__main__":
    print(json.dumps([analyze(p) for p in sys.argv[1:]], indent=2))
