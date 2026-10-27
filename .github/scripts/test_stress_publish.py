"""Exercise stress publication with a real local Git remote and a fake deployment."""

import os
from pathlib import Path
import subprocess
import tempfile
import unittest


PUBLISH = Path(__file__).with_name("publish_stress_docs.sh").resolve()


class StressPublicationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.remote = self.root / "origin.git"
        self.checkout = self.root / "checkout"
        self.environment = {
            **os.environ,
            # Keep signing and developer hooks out of the isolated Git fixtures.
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
        self.page = self.checkout / "docs/docs/stress-tests.md"
        self.page.parent.mkdir(parents=True)
        self.page.write_text("already published\n", encoding="utf-8")
        self.git("add", ".")
        self.git("commit", "-m", "initial page")
        self.git("push", "origin", "main")
        self.original_head = self.git("rev-parse", "HEAD").stdout.strip()

        bin_dir = self.root / "bin"
        bin_dir.mkdir()
        self.calls = self.root / "gh-calls.log"
        gh = bin_dir / "gh"
        gh.write_text(
            '#!/usr/bin/env bash\n'
            'printf "%s\\n" "$*" >> "$GH_CALLS"\n'
            'if [[ "$1 $2" == "workflow run" ]]; then\n'
            '  echo "https://github.com/example/repo/actions/runs/123"\n'
            'else\n'
            '  exit "${GH_WATCH_STATUS:-0}"\n'
            'fi\n',
            encoding="utf-8",
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

    def publish(self):
        return subprocess.run(
            ["bash", str(PUBLISH)], cwd=self.checkout, env=self.environment,
            text=True, capture_output=True,
        )

    def assert_deployed(self):
        self.assertEqual(
            self.calls.read_text(encoding="utf-8").splitlines(),
            ["workflow run docs.yml --ref main", "run watch 123 --exit-status"],
        )

    def test_changed_page_is_pushed_then_deployed(self):
        self.page.write_text("new measurement\n", encoding="utf-8")
        result = self.publish()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        remote_page = self.git("--git-dir", str(self.remote), "show", "main:docs/docs/stress-tests.md")
        self.assertEqual(remote_page.stdout, "new measurement\n")
        self.assert_deployed()

    def test_unchanged_page_retries_deployment_without_a_commit(self):
        result = self.publish()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(self.git("rev-parse", "HEAD").stdout.strip(), self.original_head)
        self.assert_deployed()

    def test_deployment_failure_is_reported_for_an_unchanged_page(self):
        self.environment["GH_WATCH_STATUS"] = "7"
        result = self.publish()
        self.assertEqual(result.returncode, 7, result.stdout + result.stderr)
        self.assert_deployed()


if __name__ == "__main__":
    unittest.main()
