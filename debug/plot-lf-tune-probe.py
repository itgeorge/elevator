#!/usr/bin/env python3
"""Plot LF tune probe JSON files from debug/lf-tune-probes/."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path


def load_probes(directory: Path) -> list[dict]:
    probes = []
    for path in sorted(directory.glob("*.json")):
        with path.open("r", encoding="utf-8-sig") as handle:
            probes.append(json.load(handle))
    return probes


def plot_probes(probes: list[dict], output_path: Path | None) -> None:
    try:
        import matplotlib.pyplot as plt
    except ImportError:
        print("matplotlib is required: pip install matplotlib", file=sys.stderr)
        sys.exit(1)

    if not probes:
        print(f"No probe JSON files found.", file=sys.stderr)
        sys.exit(1)

    fig, axes = plt.subplots(2, 1, figsize=(10, 8), sharex=True)

    for probe in probes:
        label = probe["label"]
        samples = probe["samples"]
        if not samples:
            continue

        xs = [sample["elapsedMs"] for sample in samples]
        ys = [sample["millivolts"] for sample in samples]
        peaks = [sample["runningPeakMv"] for sample in samples]

        axes[0].plot(xs, ys, marker="o", markersize=2, linewidth=1, label=label)
        axes[1].plot(xs, peaks, linewidth=1.5, label=f"{label} peak")

    axes[0].set_ylabel("mV (sample)")
    axes[0].set_title("LF tune sample readings")
    axes[0].grid(True, alpha=0.3)
    axes[0].legend(fontsize=8)

    axes[1].set_xlabel("elapsed ms")
    axes[1].set_ylabel("running peak mV")
    axes[1].set_title("Running peak stabilization")
    axes[1].grid(True, alpha=0.3)
    axes[1].legend(fontsize=8)

    fig.tight_layout()

    if output_path is None:
        plt.show()
    else:
        fig.savefig(output_path, dpi=150)
        print(f"Wrote {output_path}")


def summarize(probes: list[dict]) -> None:
    print("label,samples,peak_mv,last_sample_mv,last_peak_mv,ms_to_99pct_peak")
    for probe in probes:
        samples = probe.get("samples") or []
        if not samples:
            continue
        peak = probe.get("peakMillivolts", 0)
        threshold = peak * 0.99
        ms_to_stable = next(
            (sample["elapsedMs"] for sample in samples if sample["runningPeakMv"] >= threshold),
            samples[-1]["elapsedMs"],
        )
        print(
            f"{probe['label']},{len(samples)},{peak},"
            f"{samples[-1]['millivolts']},{samples[-1]['runningPeakMv']},{ms_to_stable}"
        )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "directory",
        nargs="?",
        default="debug/lf-tune-probes",
        help="Directory containing probe JSON files",
    )
    parser.add_argument("--output", "-o", help="Write PNG instead of showing interactively")
    parser.add_argument("--summary", action="store_true", help="Print stabilization summary table")
    args = parser.parse_args()

    directory = Path(args.directory)
    if not directory.is_dir():
        print(f"Directory not found: {directory}", file=sys.stderr)
        return 1

    probes = load_probes(directory)
    if args.summary:
        summarize(probes)
    if args.summary and args.output is None:
        return 0
    plot_probes(probes, Path(args.output) if args.output else None)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
