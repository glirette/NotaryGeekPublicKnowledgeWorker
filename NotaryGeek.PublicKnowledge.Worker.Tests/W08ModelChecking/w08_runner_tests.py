"""Runner failure-path checks and deliberately illegal observer states."""

import json
import os
from pathlib import Path
import select
import signal
import sys
import tempfile
import time
import unittest
from unittest.mock import Mock, patch

from w08_authority import AMBIENT_IDENTITY_VARIABLES, Authority
from w08_explore import Child, Observer, Session, Violation, replay_job, source_binding


class RunnerTests(unittest.TestCase):
    def setUp(self):
        self.environment = patch.dict(os.environ, {
            **{key: "" for key in AMBIENT_IDENTITY_VARIABLES}, "PK_TEST_STORAGE_CONNECTION": "",
        })
        self.environment.start()
        self.addCleanup(self.environment.stop)
        directory = tempfile.TemporaryDirectory(prefix="w08-runner-")
        self.addCleanup(directory.cleanup)
        self.directory = Path(directory.name)

    def test_failed_initial_spec_write_reaps_unregistered_process(self):
        process = Mock()
        process.pid = 12345
        process.stdin.write.side_effect = BrokenPipeError("synthetic closed pipe")
        with patch("w08_explore.subprocess.Popen", return_value=process), \
                patch("w08_explore.os.set_blocking"), patch("w08_explore.os.killpg") as kill:
            with self.assertRaises(BrokenPipeError):
                Child("constructor-fault", ["unused"], {})
        kill.assert_called_once_with(12345, signal.SIGKILL)
        process.wait.assert_called_once_with(timeout=5)
        for stream in (process.stdin, process.stdout, process.stderr):
            stream.close.assert_called_once()

    def test_stdout_eof_with_live_process_cannot_block_on_stderr(self):
        script = "import os,sys,time; sys.stdin.readline(); os.close(1); time.sleep(30)"
        child = Child("closed-stdout", [sys.executable, "-c", script], {})
        try:
            ready, _, _ = select.select([child.p.stdout], [], [], 10)
            self.assertTrue(ready, "child did not reach the EOF boundary")
            started = time.monotonic()
            with self.assertRaisesRegex(RuntimeError, "ended without result"):
                child.receive()
            self.assertLess(time.monotonic() - started, 2)
        finally:
            child.close()
        self.assertEqual(-signal.SIGKILL, child.p.returncode)
        self.assertTrue(child.p.stdin.closed and child.p.stdout.closed and child.p.stderr.closed)

    def test_close_reaps_process_and_preserves_already_received_result(self):
        script = ("import json,sys,time; sys.stdin.readline(); "
                  "print(json.dumps({'kind':'result','ok':True,'result':42}),flush=True); time.sleep(30)")
        child = Child("complete", [sys.executable, "-c", script], {})
        try:
            ready, _, _ = select.select([child.p.stdout], [], [], 10)
            self.assertTrue(ready)
            child.receive()
            self.assertEqual(42, child.result["result"])
        finally:
            child.close()
        child.close()  # idempotent cleanup must not target a reused PID/group.
        self.assertEqual(-signal.SIGKILL, child.p.returncode)
        self.assertTrue(child.result["ok"])

    def test_session_cleanup_continues_after_one_child_cleanup_error(self):
        session = Session.__new__(Session)
        first, second, authority = Mock(), Mock(), Mock()
        first.close.side_effect = RuntimeError("synthetic cleanup error")
        session.children, session.authority = [first, second], authority
        with self.assertRaisesRegex(RuntimeError, "synthetic cleanup error"):
            session.close()
        second.close.assert_called_once()
        authority.close.assert_called_once()

    def test_source_binding_detects_dirty_source_and_stale_binaries(self):
        root = self.directory
        production = root / "NotaryGeek.PublicKnowledge.Worker"
        harness = root / "Tests" / "W08"
        output = root / "build"
        for directory in (production, harness, output):
            directory.mkdir(parents=True)
        source = production / "Service.cs"
        source.write_text("public class Service {}")
        (harness / "worker.cs.fixture").write_text("synthetic entry point")
        worker = output / "W08Worker.dll"
        worker.write_bytes(b"worker executable")
        compiled = output / "NotaryGeek.PublicKnowledge.Worker.dll"
        compiled.write_bytes(b"production executable")
        initial = source_binding(worker, root, harness)
        self.assertEqual(initial, source_binding(worker, root, harness))
        source.write_text("public class ChangedService {}")
        changed_source = source_binding(worker, root, harness)
        self.assertNotEqual(initial["digest"], changed_source["digest"])
        compiled.write_bytes(b"a stale or changed production executable")
        self.assertNotEqual(changed_source["digest"], source_binding(worker, root, harness)["digest"])

    def test_emitted_artifact_replay_extracts_decisions_and_rejects_other_code(self):
        binding = {"algorithm": "sha256", "digest": "synthetic source hash"}
        artifact = {
            "source_head": "synthetic commit", "starting_head": "synthetic prior commit",
            "source_binding": binding, "bounds": {"actions": 3500},
            "scenario": "duplicate", "seed": 17, "policy": "random", "fault": None,
            "result": {"decisions": [0, 1, 0], "failure": None},
        }
        self.assertEqual({"scenario": "duplicate", "seed": 17, "policy": "random",
                          "fault": None, "prefix": [0, 1, 0]}, replay_job(artifact, binding))
        with self.assertRaisesRegex(ValueError, "binding differs"):
            replay_job(artifact, {**binding, "digest": "changed"})
        artifact["result"]["decisions"] = [0, "1"]
        with self.assertRaisesRegex(ValueError, "scheduler decisions"):
            replay_job(artifact, binding)

    def test_observer_rejects_duplicate_opaque_effect_identity(self):
        authority = Authority(self.directory / "authority.sqlite")
        observer = Observer()
        identity = {"job": "public-job", "case": "public-case"}
        authority.effect(identity)
        observer.check(authority)
        authority.effect(identity)
        with self.assertRaisesRegex(Violation, "provider-repeated"):
            observer.check(authority)

    def test_observer_rejects_illegal_publication_and_immutable_overwrite(self):
        authority = Authority(self.directory / "authority.sqlite")
        authority.seed("runs/executions/job/case.json", json.dumps({"indexPublished": True}))
        with self.assertRaisesRegex(Violation, "index-without-candidate"):
            Observer().check(authority)
        authority.seed("runs/executions/job/case.json", "{}")
        archive = "runs/2026/10/02/public-job/case.json"
        authority.seed(archive, '{"value":1}')
        observer = Observer()
        observer.check(authority)
        authority.seed(archive, '{"value":2}')
        with self.assertRaisesRegex(Violation, "immutable-overwrite"):
            observer.check(authority)

    def test_observer_rejects_success_receipt_downgrade(self):
        authority = Authority(self.directory / "authority.sqlite")
        archive = "runs/2026/10/02/public-job/case.json"
        authority.seed(archive, "{}")
        receipt = {"caseId": "case", "ok": True, "blobName": archive}
        job = {"jobId": "job", "status": "publishing", "receipts": [receipt]}
        authority.seed("runs/jobs/job.json", json.dumps(job))
        observer = Observer()
        observer.check(authority)
        receipt["ok"] = False
        authority.seed("runs/jobs/job.json", json.dumps(job))
        with self.assertRaisesRegex(Violation, "successful-receipt-downgraded"):
            observer.check(authority)


if __name__ == "__main__":
    unittest.main()
