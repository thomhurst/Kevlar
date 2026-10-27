"""Exercise publishers with local Git remotes and a fake GitHub deployment."""

import os
from pathlib import Path
import subprocess
import tempfile
import unittest

import yaml


SCRIPTS = Path(__file__).resolve().parent
PUBLISH = SCRIPTS / "publish_measurement_docs.sh"
WORKFLOWS = SCRIPTS.parent / "workflows"


class PublicationCases:
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.remote = self.root / "origin.git"
        self.checkout = self.root / "checkout"
        self.environment = {
            **os.environ,
            # Fixtures must not run developer hooks or require commit signing.
            "GIT_CONFIG_COUNT": "2",
            "GIT_CONFIG_KEY_0": "commit.gpgsign",
            "GIT_CONFIG_VALUE_0": "false",
            "GIT_CONFIG_KEY_1": "core.hooksPath",
            "GIT_CONFIG_VALUE_1": str(self.root / "no-hooks"),
        }
        self.git("init", "--bare", "--initial-branch=main", str(self.remote))
        self.git("clone", str(self.remote), str(self.checkout))
        self.git("config", "user.name", "Test")
        self.git("config", "user.email", "test@example.invalid")
        for path in self.paths:
            self.write(path, "already published\n")
        self.git("add", ".")
        self.git("commit", "-m", "initial measurements")
        self.git("push", "origin", "main")
        self.original_head = self.head()

        bin_dir = self.root / "bin"
        bin_dir.mkdir()
        self.calls = self.root / "gh-calls.log"
        gh = bin_dir / "gh"
        gh.write_text(
            '#!/usr/bin/env bash\n'
            'printf "%s\\n" "$*" >> "$GH_CALLS"\n'
            'case "$1 $2" in\n'
            '  "workflow run")\n'
            '    if [[ "${GH_DISPATCH_STATUS:-0}" != 0 ]]; then\n'
            '      exit "$GH_DISPATCH_STATUS"\n'
            '    fi\n'
            '    echo "https://github.com/example/repo/actions/runs/123"\n'
            '    ;;\n'
            '  "run watch") exit "${GH_WATCH_STATUS:-0}" ;;\n'
            '  *) exit 99 ;;\n'
            'esac\n',
            encoding="utf-8", newline="\n",
        )
        gh.chmod(0o755)
        self.environment.update({
            "PATH": str(bin_dir) + os.pathsep + os.environ["PATH"],
            "GH_CALLS": str(self.calls),
        })

    def git(self, *args):
        return subprocess.run(
            ["git", *args], cwd=self.checkout if self.checkout.exists() else self.root,
            check=True, text=True, capture_output=True, env=self.environment,
        )

    def head(self):
        return self.git("rev-parse", "HEAD").stdout.strip()

    def remote_head(self):
        return self.git("--git-dir", str(self.remote), "rev-parse", "main").stdout.strip()

    def remote_content(self, path):
        return self.git("--git-dir", str(self.remote), "show", f"main:{path}").stdout

    def write(self, path, content):
        target = self.checkout / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding="utf-8", newline="\n")

    def publish(self):
        return subprocess.run(
            ["bash", str(PUBLISH), self.kind], cwd=self.checkout,
            env=self.environment, text=True, capture_output=True,
        )

    def assert_published(self):
        result = self.publish()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(self.head(), self.remote_head())

    def assert_deployed(self):
        self.assertEqual(
            self.calls.read_text(encoding="utf-8").splitlines(),
            ["workflow run docs.yml --ref main", "run watch 123 --exit-status"],
        )

    def test_changed_measurements_are_pushed_together_then_deployed(self):
        for path in self.paths:
            self.write(path, "new measurement from original event SHA\n")
        self.assert_published()
        self.assertNotEqual(self.head(), self.original_head)
        self.assertEqual(
            set(self.git("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD").stdout.splitlines()),
            set(self.paths),
        )
        for path in self.paths:
            self.assertEqual(self.remote_content(path), "new measurement from original event SHA\n")
        self.assert_deployed()

    def test_unchanged_measurements_redeploy_without_commit_or_push(self):
        # Reject any push: unchanged publication must still deploy successfully.
        hook = self.remote / "hooks/pre-receive"
        hook.write_text("#!/usr/bin/env bash\nexit 1\n", encoding="utf-8", newline="\n")
        hook.chmod(0o755)
        self.assert_published()
        self.assertEqual(self.head(), self.original_head)
        self.assert_deployed()

    def test_rerun_uses_current_main_after_an_earlier_publication(self):
        # Reproduce the previous attempt having already changed this same page.
        self.write(self.paths[0], "earlier publication\n")
        self.git("add", ".")
        self.git("commit", "-m", "previous publication")
        self.git("push", "origin", "main")
        # Match checkout@v7's explicit main ref instead of the stale event SHA.
        self.git("checkout", "--detach", self.original_head)
        self.git("fetch", "origin", "main")
        self.git("checkout", "--detach", "origin/main")
        self.write(self.paths[0], "rerun publication\n")
        self.assert_published()
        self.assertEqual(self.remote_content(self.paths[0]), "rerun publication\n")
        self.assert_deployed()

    def test_unrelated_main_changes_are_retained(self):
        self.write("README.md", "concurrent unrelated update\n")
        self.git("add", "README.md")
        self.git("commit", "-m", "unrelated main change")
        self.git("push", "origin", "main")
        self.git("reset", "--hard", self.original_head)
        self.write(self.paths[0], "new measurements\n")
        self.assert_published()
        self.assertEqual(self.remote_content("README.md"), "concurrent unrelated update\n")
        self.assertEqual(self.remote_content(self.paths[0]), "new measurements\n")
        self.assert_deployed()

    def test_committed_measurements_can_redeploy_after_failure(self):
        self.write(self.paths[0], "new measurements\n")
        self.environment["GH_WATCH_STATUS"] = "7"
        result = self.publish()
        self.assertEqual(result.returncode, 7, result.stdout + result.stderr)
        self.assertEqual(self.remote_content(self.paths[0]), "new measurements\n")
        self.assert_deployed()
        committed_head = self.head()
        self.environment.pop("GH_WATCH_STATUS")
        self.calls.unlink()
        self.assert_published()
        self.assertEqual(self.head(), committed_head)
        self.assert_deployed()

    def test_deployment_failure_is_reported_without_a_commit(self):
        self.environment["GH_WATCH_STATUS"] = "7"
        result = self.publish()
        self.assertEqual(result.returncode, 7, result.stdout + result.stderr)
        self.assertEqual(self.remote_head(), self.original_head)
        self.assert_deployed()

    def test_failed_dispatch_does_not_start_a_watch(self):
        self.environment["GH_DISPATCH_STATUS"] = "8"
        result = self.publish()
        self.assertEqual(result.returncode, 8, result.stdout + result.stderr)
        self.assertEqual(self.calls.read_text(encoding="utf-8").splitlines(),
                         ["workflow run docs.yml --ref main"])

    def test_failed_push_does_not_deploy(self):
        self.write(self.paths[0], "new measurements\n")
        hook = self.remote / "hooks/pre-receive"
        hook.write_text("#!/usr/bin/env bash\nexit 1\n", encoding="utf-8", newline="\n")
        hook.chmod(0o755)
        result = self.publish()
        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertFalse(self.calls.exists())
        self.assertEqual(self.remote_head(), self.original_head)


class BenchmarkPublicationTests(PublicationCases, unittest.TestCase):
    kind = "benchmark"
    paths = ("docs/docs/benchmarks.md",)


class OutagePublicationTests(PublicationCases, unittest.TestCase):
    kind = "outage"
    paths = ("docs/docs/outage-recovery.md", "docs/static/measurements/outage-results.json")

    def test_raw_data_only_change_is_published(self):
        self.write(self.paths[1], "changed raw measurements\n")
        self.assert_published()
        self.assertEqual(self.remote_content(self.paths[0]), "already published\n")
        self.assertEqual(self.remote_content(self.paths[1]), "changed raw measurements\n")
        self.assert_deployed()


class WorkflowPolicyTests(unittest.TestCase):
    def publisher(self, filename):
        workflow = yaml.safe_load((WORKFLOWS / filename).read_text(encoding="utf-8"))
        return workflow["jobs"]["publish-docs"]

    def test_publishers_checkout_current_main_under_separate_job_locks(self):
        groups = set()
        for filename, kind in (("benchmarks.yml", "benchmark"), ("outage.yml", "outage")):
            with self.subTest(workflow=filename):
                job = self.publisher(filename)
                self.assertEqual(job["steps"][0]["with"]["ref"], "main")
                self.assertFalse(job["concurrency"]["cancel-in-progress"])
                groups.add(job["concurrency"]["group"])
                self.assertEqual(job["steps"][-1]["run"],
                                 f"bash .github/scripts/publish_measurement_docs.sh {kind}")
        self.assertEqual(len(groups), 2)

    def test_benchmark_publication_requires_a_full_main_run(self):
        expression = self.publisher("benchmarks.yml")["if"]
        for event in ("push", "schedule", "workflow_dispatch"):
            for ref in ("refs/heads/main", "refs/heads/feature"):
                for benchmark_filter in ("", "*", "*Retry*"):
                    for baseline in ("", "a" * 40):
                        with self.subTest(event=event, ref=ref, filter=benchmark_filter, baseline=baseline):
                            condition = expression
                            for key, value in {
                                "github.ref": ref, "github.event_name": event,
                                "inputs.benchmark_filter": benchmark_filter,
                                "inputs.baseline_sha": baseline,
                            }.items():
                                condition = condition.replace(key, repr(value))
                            condition = " ".join(condition.replace("&&", "and").replace("||", "or").split())
                            self.assertEqual(
                                eval(condition, {"__builtins__": {}}),
                                ref == "refs/heads/main" and baseline == "" and
                                (event != "workflow_dispatch" or benchmark_filter in ("", "*")),
                            )

    def test_generation_preserves_measurement_attribution_and_outage_input(self):
        benchmark_steps = self.publisher("benchmarks.yml")["steps"]
        generate = next(step["run"] for step in benchmark_steps if step.get("name") == "Generate Benchmarks Page")
        self.assertIn('--commit "${GITHUB_SHA::7}"', generate)
        outage_steps = self.publisher("outage.yml")["steps"]
        generate = next(step["run"] for step in outage_steps if step.get("name") == "Generate page")
        copy_raw = next(step["run"] for step in outage_steps if step.get("name") == "Publish matching raw data")
        self.assertIn("--input outage-results/outage-results.json", generate)
        self.assertIn("cp outage-results/outage-results.json docs/static/measurements/outage-results.json", copy_raw)


if __name__ == "__main__":
    unittest.main()
