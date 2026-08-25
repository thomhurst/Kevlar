"""Generate docs/docs/benchmarks.md from BenchmarkDotNet results.

Reads the ``*-report-full-compressed.json`` files produced by ``--exporters json``,
pairs ``Kevlar_X`` / ``Polly_X`` methods within each benchmark class, and writes a
Docusaurus page. When a benchmark history file (github-action-benchmark's
``data.js``) is supplied, the "vs Polly" ratio is the median over the last N runs
so a single noisy CI run cannot swing the published numbers.
"""

import argparse
import glob
import json
import os
import re
from datetime import datetime, timezone
from pathlib import Path
from statistics import median

DEFAULT_WINDOW = 10

# Ratios inside this band are indistinguishable from CI noise.
_PARITY_LOW = 0.83
_PARITY_HIGH = 1.20

_DATA_PREFIX = "window.BENCHMARK_DATA = "
_METHOD = re.compile(r"^(?P<library>Kevlar|Polly)_(?P<scenario>\w+)$")

# Class name -> (section title, sort order, blurb)
_SECTIONS = {
    "OverheadBenchmarks": (
        "Pipeline overhead floor",
        0,
        "What an empty pipeline costs per execution — the fixed tax every strategy builds on.",
    ),
    "RetryBenchmarks": (
        "Retry",
        1,
        "Happy path (judge overhead only) and a recovery path where every call fails twice "
        "before succeeding, with backoff disabled so only strategy machinery is measured.",
    ),
    "TimeoutBenchmarks": (
        "Timeout",
        2,
        "The timeout never fires; this is the cost of arming and disarming the cancellation "
        "plumbing on every call.",
    ),
    "CircuitBreakerBenchmarks": (
        "Circuit breaker",
        3,
        "Ratio/sampling bookkeeping while closed, and the fast-fail rejection cost while "
        "manually isolated (thrown exception included).",
    ),
    "HedgingBenchmarks": (
        "Hedging",
        4,
        "The primary attempt completes synchronously, so the hedge timer never fires.",
    ),
    "FallbackBenchmarks": (
        "Fallback",
        5,
        "Pass-through when the execution succeeds, and substitution when it throws.",
    ),
    "RateLimitBenchmarks": (
        "Rate limit",
        6,
        "Uncontended token-bucket permit acquisition — every call is admitted.",
    ),
    "ConcurrencyLimitBenchmarks": (
        "Concurrency limit",
        7,
        "A single caller against a large permit count; acquire/release cost with no queueing.",
    ),
    "TypedResultBenchmarks": (
        "Typed result handling",
        8,
        "Retry configured to treat a sentinel result as a failure; the returned value never "
        "matches, so this is the per-call cost of judging results.",
    ),
    "PipelineBenchmarks": (
        "Composed pipelines",
        9,
        "How per-call overhead scales with pipeline depth when nothing goes wrong.",
    ),
}

_SCENARIO_LABELS = {
    ("OverheadBenchmarks", "Empty"): "Empty pipeline — async",
    ("OverheadBenchmarks", "EmptyState"): "Empty pipeline — zero-closure state overload",
    ("OverheadBenchmarks", "EmptyReferenceState"): "Empty pipeline — reference-state baseline",
    ("OverheadBenchmarks", "EmptyContextState"): "Empty pipeline — caller-seeded context",
    ("OverheadBenchmarks", "EmptySync"): "Empty pipeline — sync",
    ("RetryBenchmarks", "HappyPath"): "Retry(3) — success on first attempt",
    ("RetryBenchmarks", "Recovery"): "Retry(3) — two failures then success",
    ("TimeoutBenchmarks", "HappyPath"): "Timeout(10 s) — completes instantly",
    ("CircuitBreakerBenchmarks", "ClosedHappyPath"): "Closed circuit — success",
    ("CircuitBreakerBenchmarks", "OpenFastFail"): "Open circuit — fast-fail rejection",
    ("CircuitBreakerBenchmarks", "RatioClosedHappyPath"): "Ratio breaker, closed — success",
    ("CircuitBreakerBenchmarks", "IsolatedFastFail"): "Isolated circuit — fast-fail rejection",
    ("HedgingBenchmarks", "PrimaryWins"): "Hedge(2) — primary wins",
    ("FallbackBenchmarks", "PassThrough"): "Fallback — not triggered",
    ("FallbackBenchmarks", "Triggered"): "Fallback — triggered by exception",
    ("RateLimitBenchmarks", "Uncontended"): "Rate limit — uncontended acquire",
    ("RateLimitBenchmarks", "TokenBucketUncontended"): "Token bucket — uncontended acquire",
    ("ConcurrencyLimitBenchmarks", "Uncontended"): "Concurrency limit — uncontended",
    ("TypedResultBenchmarks", "ResultJudged"): "Typed retry — result judged, no retry",
    ("PipelineBenchmarks", "TimeoutRetryBreaker"): "Timeout → Retry → Circuit breaker",
    ("PipelineBenchmarks", "FiveStrategyChain"): "Five-strategy chain",
    ("PipelineBenchmarks", "RatioTimeoutRetryBreaker"): "Timeout → Retry → ratio breaker",
    ("PipelineBenchmarks", "TokenBucketRatioFiveStrategyChain"): (
        "Token bucket → Timeout → Retry → ratio breaker → Concurrency limit"
    ),
}


def load_results(results_dir):
    """Parse every full JSON report below results_dir.

    Returns ({class_name: {scenario: {library: bench}}}, host_info).
    """
    classes = {}
    host_info = None
    pattern = os.path.join(results_dir, "**", "*-report-full-compressed.json")
    files = sorted(glob.glob(pattern, recursive=True))
    if not files:
        raise SystemExit(f"No *-report-full-compressed.json files found under {results_dir}")

    for path in files:
        with open(path, encoding="utf-8") as fp:
            data = json.load(fp)
        if host_info is None:
            host_info = data.get("HostEnvironmentInfo", {})
        for bench in data.get("Benchmarks", []):
            match = _METHOD.match(bench.get("Method", ""))
            stats = bench.get("Statistics") or {}
            if match is None or stats.get("Median") is None:
                continue
            cls = bench.get("Type", "")
            scenario = match.group("scenario")
            memory = bench.get("Memory") or {}
            classes.setdefault(cls, {}).setdefault(scenario, {})[match.group("library")] = {
                "median_ns": stats["Median"],
                "allocated": memory.get("BytesAllocatedPerOperation"),
                "full_name": bench.get("FullName", ""),
            }
    return classes, host_info or {}


def load_history(path):
    text = Path(path).read_text(encoding="utf-8").strip()
    if not text.startswith(_DATA_PREFIX):
        raise ValueError("Benchmark history does not start with window.BENCHMARK_DATA")
    payload = text[len(_DATA_PREFIX):].rstrip(";").strip()
    data = json.loads(payload)
    return data.get("entries", {}).get("Kevlar Benchmarks", [])


def rolling_ratio(entries, kevlar_name, polly_name, window):
    """Median of polly/kevlar over the last `window` runs that measured both."""
    ratios = []
    for entry in entries:
        values = {b.get("name"): b.get("value") for b in entry.get("benches", [])}
        kevlar = values.get(kevlar_name)
        polly = values.get(polly_name)
        if isinstance(kevlar, (int, float)) and isinstance(polly, (int, float)) and kevlar:
            ratios.append(polly / kevlar)
    recent = ratios[-window:]
    return (median(recent), len(recent)) if recent else (None, 0)


def fmt_time(ns):
    if ns is None:
        return "—"
    if ns < 1_000:
        return f"{ns:.1f} ns" if ns < 100 else f"{ns:.0f} ns"
    if ns < 1_000_000:
        return f"{ns / 1_000:.2f} μs"
    return f"{ns / 1_000_000:.2f} ms"


def fmt_bytes(value):
    if value is None:
        return "—"
    if value == 0:
        return "0 B"
    if value < 1024:
        return f"{value:g} B"
    return f"{value / 1024:.1f} KB"


def _times(value):
    return f"{value:.0f}×" if value >= 9.95 else f"{value:.1f}×"


def describe_ratio(ratio):
    """ratio = Polly time / Kevlar time."""
    if ratio is None:
        return "—"
    if ratio >= _PARITY_HIGH:
        return f"**{_times(ratio)} faster**"
    if ratio <= _PARITY_LOW:
        return f"{_times(1 / ratio)} slower"
    return "on par"


def scenario_label(cls, scenario):
    return _SCENARIO_LABELS.get((cls, scenario), scenario)


def build_page(classes, host_info, history, window, commit):
    now = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M UTC")
    lines = [
        "---",
        "sidebar_position: 18",
        "sidebar_label: Benchmarks",
        "---",
        "",
        "<!-- Auto-generated by .github/scripts/benchmark_docs.py — do not edit by hand. -->",
        "",
        "# Benchmarks",
        "",
        "Kevlar vs [Polly v8](https://github.com/App-vNext/Polly) across every strategy, "
        "measured with [BenchmarkDotNet](https://benchmarkdotnet.org/) on GitHub Actions "
        "and republished automatically by the "
        "[benchmarks workflow](https://github.com/thomhurst/Kevlar/actions/workflows/benchmarks.yml).",
        "",
        f"*Last updated {now}" + (f" (commit `{commit}`)" if commit else "") + ".*",
        "",
        ":::note",
        "Microbenchmarks on shared CI runners are noisy. The **vs Polly** column is the "
        f"median ratio over the most recent runs (up to {window}); anything within ±20% "
        "is reported as *on par*. Absolute times move with runner hardware — the ratios "
        "are the signal.",
        ":::",
        "",
    ]

    ordered = sorted(
        classes.items(),
        key=lambda item: _SECTIONS.get(item[0], (item[0], 99, ""))[1],
    )

    for cls, scenarios in ordered:
        title, _, blurb = _SECTIONS.get(cls, (cls, 99, ""))
        lines.append(f"## {title}")
        lines.append("")
        if blurb:
            lines.append(blurb)
            lines.append("")
        lines.append("| Scenario | Kevlar | Polly | Kevlar allocated | Polly allocated | Kevlar vs Polly |")
        lines.append("|---|---|---|---|---|---|")
        for scenario in scenarios:
            pair = scenarios[scenario]
            kevlar = pair.get("Kevlar")
            polly = pair.get("Polly")

            ratio = None
            if kevlar and polly:
                ratio, runs = (None, 0)
                if history:
                    ratio, runs = rolling_ratio(
                        history, kevlar["full_name"], polly["full_name"], window
                    )
                if ratio is None:
                    ratio = polly["median_ns"] / kevlar["median_ns"] if kevlar["median_ns"] else None

            lines.append(
                "| {label} | {k_time} | {p_time} | {k_alloc} | {p_alloc} | {verdict} |".format(
                    label=scenario_label(cls, scenario),
                    k_time=fmt_time(kevlar["median_ns"] if kevlar else None),
                    p_time=fmt_time(polly["median_ns"] if polly else None),
                    k_alloc=fmt_bytes(kevlar["allocated"] if kevlar else None),
                    p_alloc=fmt_bytes(polly["allocated"] if polly else None),
                    verdict=describe_ratio(ratio),
                )
            )
        lines.append("")

    cpu = host_info.get("ProcessorName", "unknown CPU")
    runtime = host_info.get("RuntimeVersion", "unknown runtime")
    bdn = host_info.get("BenchmarkDotNetVersion", "")
    lines += [
        "## Environment",
        "",
        f"- {cpu}",
        f"- {runtime}" + (f", BenchmarkDotNet {bdn}" if bdn else ""),
        "- Times are medians; allocations are per operation.",
        "",
        "## Reproduce",
        "",
        "```bash",
        "dotnet run -c Release --project benchmarks/Kevlar.Benchmarks -- --filter '*'",
        "```",
        "",
        "As always with microbenchmarks: measure your own workload before optimizing "
        "around these numbers. Nanosecond differences matter in tight loops and "
        "high-throughput services; they don't matter around a 50 ms network call.",
        "",
    ]
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--results-dir", required=True)
    parser.add_argument("--history", help="Path to github-action-benchmark data.js (optional)")
    parser.add_argument("--window", type=int, default=DEFAULT_WINDOW)
    parser.add_argument("--commit", default="", help="Short commit SHA for the footer")
    parser.add_argument("--output", required=True)
    args = parser.parse_args()

    classes, host_info = load_results(args.results_dir)
    history = load_history(args.history) if args.history else []

    page = build_page(classes, host_info, history, args.window, args.commit)
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(page, encoding="utf-8", newline="\n")
    total = sum(len(s) for s in classes.values())
    print(f"Wrote {output} ({len(classes)} sections, {total} scenarios, history runs: {len(history)})")


if __name__ == "__main__":
    main()
