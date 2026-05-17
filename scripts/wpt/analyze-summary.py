#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import shlex
from collections import Counter
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Analyze Broiler.HTML non-JS WPT summary.json output."
    )
    parser.add_argument("summary", help="Path to a summary.json file produced by scripts/wpt/run-non-js.mjs.")
    parser.add_argument("--top", type=int, default=10, help="How many failing cases to print per section (default: 10).")
    parser.add_argument(
        "--emit-rerun-args",
        action="store_true",
        help="Emit --include arguments for each failing test so the batch can be rerun with a tighter focus.",
    )
    args = parser.parse_args()

    summary_path = Path(args.summary).resolve()
    summary = read_json(summary_path)
    failed = [normalize_failure(entry, summary_path) for entry in summary.get("failed", [])]
    timed_out = [entry for entry in failed if entry["timeout"]]
    visual_failures = [entry for entry in failed if not entry["timeout"]]

    print(f"Summary: {summary.get('passedCount', 0)} passed, {summary.get('failedCount', 0)} failed, {summary.get('timedOutCount', 0)} timed out.")
    print(f"Selected candidates: {summary.get('totalCandidates', 0)}")
    print()

    if timed_out:
        timeout_phases = Counter(entry["timeoutPhase"] or "unknown" for entry in timed_out)
        print("Timeout phases:")
        for phase, count in timeout_phases.most_common():
            print(f"- {phase}: {count}")
        print()

        print(f"Timed out cases (top {min(args.top, len(timed_out))}):")
        for entry in sorted(timed_out, key=timeout_sort_key, reverse=True)[:args.top]:
            duration = format_duration(entry["totalDurationMs"] or summary.get("timeouts", {}).get("perTestMs"))
            print(f"- {entry['path']} [{duration}] phase={entry['timeoutPhase'] or 'unknown'}")
        print()

    if visual_failures:
        mismatch_categories = Counter(entry["mismatchCategory"] or "unknown" for entry in visual_failures)
        print("Visual mismatch categories:")
        for category, count in mismatch_categories.most_common():
            print(f"- {category}: {count}")
        print()

        print(f"Visual mismatch details (top {min(args.top, len(visual_failures))}):")
        for entry in sorted(visual_failures, key=visual_sort_key, reverse=True)[:args.top]:
            diff_ratio = "n/a" if entry["diffRatio"] is None else f"{entry['diffRatio'] * 100:.2f}%"
            summary_text = entry["mismatchSummary"] or "No mismatch summary available."
            print(f"- {entry['path']} [{diff_ratio}] {entry['mismatchCategory'] or 'unknown'}: {summary_text}")
        print()

    if not timed_out and not visual_failures:
        print("No failures found in the supplied summary.")
        print()

    if args.emit_rerun_args and failed:
        includes = " ".join(f"--include {shell_quote(entry['path'])}" for entry in failed)
        print("Focused rerun arguments:")
        print(includes)

    return 0


def normalize_failure(entry: dict, summary_path: Path) -> dict:
    report = load_report(entry, summary_path)

    mismatch = normalize_mismatch(pick(entry, "mismatch", "Mismatch") or pick(report, "mismatch", "Mismatch"))
    diff_ratio = normalize_float(pick(entry, "diffRatio", "DiffRatio"))
    if diff_ratio is None:
        diff_ratio = normalize_float(pick(report, "diffRatio", "DiffRatio"))

    return {
        "path": pick(entry, "path", "Path"),
        "timeout": bool(pick(entry, "timeout", "Timeout") or False),
        "timeoutPhase": pick(entry, "timeoutPhase", "TimeoutPhase") or infer_timeout_phase(pick(entry, "error", "Error")),
        "totalDurationMs": normalize_float(pick(entry, "totalDurationMs", "TotalDurationMs")),
        "diffRatio": diff_ratio,
        "mismatchCategory": mismatch.get("category"),
        "mismatchSummary": mismatch.get("summary"),
    }


def load_report(entry: dict, summary_path: Path) -> dict:
    report_path = normalize_path(pick(entry, "reportPath", "ReportPath"))
    candidate_paths = []
    if report_path:
        candidate_paths.append(Path(report_path))

    entry_path = normalize_path(pick(entry, "path", "Path"))
    if entry_path:
        candidate_paths.append(summary_path.parent / "cases" / entry_path / "report.json")

    for candidate in candidate_paths:
        report_file = candidate if candidate.is_absolute() else (summary_path.parent / candidate).resolve()
        if report_file.is_file():
            return read_json(report_file)

    return {}


def normalize_mismatch(mismatch: object) -> dict:
    if not isinstance(mismatch, dict):
        return {}

    return {
        "category": pick(mismatch, "category", "Category"),
        "summary": pick(mismatch, "summary", "Summary"),
    }


def infer_timeout_phase(error: object) -> str | None:
    if not isinstance(error, str):
        return None
    if "with Broiler.HTML timed out" in error:
        return "broiler-render"
    if "Chromium reference" in error:
        return "chromium-reference"
    if error.startswith("Compare "):
        return "image-compare"
    return None


def pick(obj: object, *names: str):
    if not isinstance(obj, dict):
        return None

    for name in names:
        if name in obj and obj[name] is not None:
            return obj[name]
    return None


def normalize_float(value: object) -> float | None:
    if isinstance(value, (int, float)):
        return float(value)
    if isinstance(value, str) and value.strip():
        try:
            return float(value)
        except ValueError:
            return None
    return None


def normalize_path(value: object) -> str | None:
    return value if isinstance(value, str) and value else None


def read_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def timeout_sort_key(entry: dict) -> tuple[float, str]:
    return (entry["totalDurationMs"] or 0.0, entry["path"] or "")


def visual_sort_key(entry: dict) -> tuple[float, str]:
    return (entry["diffRatio"] or 0.0, entry["path"] or "")


def format_duration(duration_ms: float | None) -> str:
    if duration_ms is None:
        return "n/a"
    if duration_ms >= 1000:
        return f"{duration_ms / 1000:.1f}s"
    return f"{duration_ms:.0f}ms"


def shell_quote(value: str) -> str:
    return shlex.quote(value)


if __name__ == "__main__":
    raise SystemExit(main())
