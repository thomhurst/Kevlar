"""Run a commit comparison sequentially on one Actions runner and retain evidence."""

import datetime
import json
import math
import os
from pathlib import Path
import re
import shlex
import subprocess


def run(command, cwd, log):
    with log.open("w", encoding="utf-8") as output:
        result = subprocess.run(command, cwd=cwd, stdout=output, stderr=subprocess.STDOUT)
    if result.returncode:
        print(log.read_text(encoding="utf-8")[-12000:], flush=True)
        raise RuntimeError(f"Command failed ({result.returncode}); see {log}")


def read_results(directory):
    results = {}
    for report in sorted(directory.glob("results/*-report-full-compressed.json")):
        for benchmark in json.loads(report.read_text())["Benchmarks"]:
            name = benchmark["FullName"]
            statistics = benchmark.get("Statistics")
            if not statistics or not all(
                math.isfinite(statistics[field]) and statistics[field] > 0
                for field in ("Mean", "Median")
            ):
                raise RuntimeError(f"Missing or invalid measurements: {name}")
            if name in results:
                raise RuntimeError(f"Duplicate benchmark: {name}")
            results[name] = benchmark
    if not results:
        raise RuntimeError(f"No successful benchmark reports in {directory}")
    return results


def main():
    repository = Path.cwd()
    root = Path(os.environ["RUNNER_TEMP"]) / "benchmark-aba"
    evidence = root / "results"
    evidence.mkdir(parents=True, exist_ok=False)
    commits = {"A": os.environ["BASELINE_SHA"], "B": os.environ["CANDIDATE_SHA"]}
    if any(not re.fullmatch(r"[0-9a-fA-F]{40}", sha) for sha in commits.values()):
        raise ValueError("Both refs must be full immutable commit SHAs")
    filters = shlex.split(os.environ["BENCHMARK_FILTER"])
    if not filters or any(value.startswith("-") for value in filters):
        raise ValueError("Provide one or more space-separated benchmark glob filters")
    metadata = {
        "commits": commits,
        "filters": filters,
        "runner": {key: os.environ.get(key) for key in (
            "RUNNER_NAME", "RUNNER_OS", "RUNNER_ARCH", "ImageOS", "ImageVersion",
            "GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT",
        )},
        "packages": {},
        "phases": [],
    }
    run(["dotnet", "--info"], repository, evidence / "dotnet-info.log")
    run(["lscpu"], repository, evidence / "cpu-info.log")
    project = Path("benchmarks/Kevlar.Benchmarks")
    command = ["dotnet", "run", "-c", "Release", "--no-build", "--",
               "--filter", *filters, "--exporters", "json"]
    worktrees = {}
    dry_results = {}
    for label, sha in commits.items():
        worktree = root / label
        worktrees[label] = worktree
        run(["git", "worktree", "add", "--detach", str(worktree), sha],
            repository, evidence / f"{label}-checkout.log")
        run(["dotnet", "build", str(project / "Kevlar.Benchmarks.csproj"), "-c", "Release"],
            worktree, evidence / f"{label}-build.log")
        assets_log = evidence / f"{label}-assets-path.log"
        run(["dotnet", "msbuild", str(project / "Kevlar.Benchmarks.csproj"),
             "-p:Configuration=Release", "-getProperty:ProjectAssetsFile"], worktree, assets_log)
        assets = json.loads(Path(assets_log.read_text().strip()).read_text())
        packages = sorted(key for key, value in assets["libraries"].items()
                          if value["type"] == "package")
        metadata["packages"][label] = packages
        print(f"{label}: {sha}; Reservoir: {[p for p in packages if p.startswith('Reservoir/')]}",
              flush=True)
        run([*command, "--job", "Dry", "--artifacts", str(evidence / f"{label}-dry")],
            worktree / project, evidence / f"{label}-dry.log")
        dry_results[label] = read_results(evidence / f"{label}-dry")
    if dry_results["A"].keys() != dry_results["B"].keys():
        raise RuntimeError("Baseline and candidate select different benchmark cases")
    (evidence / "metadata.json").write_text(json.dumps(metadata, indent=2))
    measured = {}
    for phase, label in (("A1", "A"), ("B", "B"), ("A2", "A")):
        timing = {"phase": phase, "start": datetime.datetime.now(datetime.timezone.utc).isoformat()}
        print(f"Starting {phase}: {len(dry_results[label])} benchmarks", flush=True)
        run([*command, "--artifacts", str(evidence / phase)],
            worktrees[label] / project, evidence / f"{phase}.log")
        measured[phase] = read_results(evidence / phase)
        if measured[phase].keys() != dry_results[label].keys():
            raise RuntimeError(f"Incomplete results for {phase}")
        timing["end"] = datetime.datetime.now(datetime.timezone.utc).isoformat()
        metadata["phases"].append(timing)
        (evidence / "metadata.json").write_text(json.dumps(metadata, indent=2))
        print(f"Completed {phase}", flush=True)

    lines = ["# A-B-A benchmark comparison", "",
             f"A: `{commits['A']}`", f"B: `{commits['B']}`", "",
             "Sequential A1 → B → A2 on the same runner, using BenchmarkDotNet default jobs.",
             "Medians are ns/op; negative changes mean faster. Allocations are bytes/op.",
             "B change uses the arithmetic mean of A1 and A2 medians. Drift is A2/A1 − 1.",
             "Drift above 5% is flagged; these descriptive comparisons are not significance tests.", "",
             "| Benchmark | A1 ns | B ns | A2 ns | B change | A drift | A1/B/A2 bytes |",
             "|---|---:|---:|---:|---:|---:|---:|"]
    for name in sorted(measured["A1"]):
        rows = [measured[phase][name] for phase in ("A1", "B", "A2")]
        a1, b, a2 = (row["Statistics"]["Median"] for row in rows)
        change = (b / ((a1 + a2) / 2) - 1) * 100
        drift = (a2 / a1 - 1) * 100
        memory = "/".join(str(row.get("Memory", {}).get("BytesAllocatedPerOperation", "n/a"))
                          for row in rows)
        flag = " ⚠" if abs(drift) > 5 else ""
        lines.append(f"| {name.removeprefix('Kevlar.Benchmarks.')} | {a1:.2f} | {b:.2f} | "
                     f"{a2:.2f} | {change:+.2f}% | {drift:+.2f}%{flag} | {memory} |")
    lines.extend(["", "## Resolved package differences", ""])
    for label, other in (("A", "B"), ("B", "A")):
        difference = sorted(set(metadata["packages"][label]) - set(metadata["packages"][other]))
        lines.append(f"{label} only: {', '.join(difference) or 'none'}")
    summary = "\n".join(lines) + "\n"
    (evidence / "summary.md").write_text(summary, encoding="utf-8")
    with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as output:
        output.write(summary)
    print(summary, flush=True)


if __name__ == "__main__":
    main()
