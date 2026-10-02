#!/usr/bin/env python3
"""Bounded production scheduler. No network sockets; children use JSON lines on pipes.

The authority implements only HTTP byte/ETag primitives. This separate observer
interprets public artifacts to check the contract. A lost reply never rolls back
an authority commit. All processes are killed and waited in finally blocks.
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import random
import select
import signal
import subprocess
import tempfile
import time
from datetime import datetime
from urllib.parse import urlparse, unquote, parse_qs

from w08_authority import Authority, require_no_ambient_identity
from w08_trace_adapter import TraceAdapter

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
STARTING_HEAD = "27097b551b75d2d64a9e3f1df26c61e73a3eb792"


def digest(data):
    return hashlib.sha256(data).hexdigest()


def timestamp_order(value):
    # DateTime's seventh fractional digit must survive Python's microsecond parser.
    whole, _, fraction = value.removesuffix("Z").partition(".")
    if value.endswith("Z"):
        return (whole, fraction.ljust(7, "0"))
    parsed = datetime.fromisoformat(value)
    return (parsed.isoformat(timespec="seconds"), f"{parsed.microsecond:06d}0")


def source_binding(worker, root=ROOT, harness=HERE):
    """Bind the actual source bytes and executable bytes, including dirty files."""
    root, harness, worker = Path(root), Path(harness), Path(worker)
    files = {}
    for directory in (root / "NotaryGeek.PublicKnowledge.Worker", harness):
        for path in sorted(directory.rglob("*")):
            if (path.is_file() and not {"bin", "obj", "__pycache__"}.intersection(path.parts)
                    and path.suffix in {".cs", ".csproj", ".props", ".targets", ".py", ".fixture"}):
                files[path.relative_to(root).as_posix()] = digest(path.read_bytes())
    binaries = {worker.name: digest(worker.read_bytes())}
    # The copied production DLL is what dotnet actually loads. Hashing source
    # alone would miss a stale build even if the worker entry point were current.
    for name in ("NotaryGeek.PublicKnowledge.Worker.dll", worker.stem + ".deps.json",
                 worker.stem + ".runtimeconfig.json"):
        path = worker.parent / name
        if path.is_file():
            binaries[name] = digest(path.read_bytes())
    manifest = {"files": files, "binaries": binaries}
    return {"algorithm": "sha256", "digest": digest(json.dumps(
        manifest, sort_keys=True, separators=(",", ":")).encode()), **manifest}


def replay_job(schedule, binding):
    if schedule.get("source_binding") != binding:
        raise ValueError("Replay source/executable binding differs from the recorded schedule.")
    result = schedule.get("result", {})
    decisions = result.get("decisions")
    if not isinstance(decisions, list) or any(type(item) is not int or item < 0 for item in decisions):
        raise ValueError("Replay requires the recorded nonnegative scheduler decisions.")
    job = {key: schedule[key] for key in ("scenario", "seed", "policy", "fault") if key in schedule}
    if "scenario" not in job:
        raise ValueError("Replay requires a scenario.")
    job["prefix"] = decisions
    return job


def request_name(r):
    return unquote(urlparse(r.get("url", "")).path).removeprefix("/fixture/")


def body_json(r):
    try:
        return json.loads(base64.b64decode(r.get("body", "")))
    except (ValueError, UnicodeDecodeError):
        return {}


def boundary(r):
    if r["kind"] in ("source", "provider"):
        return r["kind"]
    if r["kind"] == "queue" and r["method"] == "POST":
        return "enqueue"
    if r["method"] != "PUT" or "restype" in parse_qs(urlparse(r["url"]).query):
        return "read"
    name, value = request_name(r), body_json(r)
    if name.startswith("runs/executions/"):
        if value.get("phase") == "provider-outcome-unknown":
            return "admission"
        if value.get("digestPublished"):
            return "digest-phase"
        if value.get("indexPublished"):
            return "index-phase"
        if value.get("candidatesPublished"):
            return "candidate-phase"
        return "evidence-phase"
    if name.startswith("runs/jobs/"):
        if value.get("publishedCaseIds"):
            return "completion"
        if value.get("receipts"):
            return "receipt"
        return "job"
    if name.startswith("runs/latest/"):
        return "latest"
    if name == "runs/latest-index.json":
        return "index"
    if name == "runs/latest-needs-greg.json":
        return "digest"
    if name.startswith("promotion/candidates/"):
        return "candidate"
    if name.startswith("runs/"):
        return "archive"
    return "other"


class Violation(AssertionError):
    def __init__(self, code, detail):
        super().__init__(f"{code}: {detail}")
        self.code = code


class Observer:
    """State oracle independent of orchestration's branches and fixture decisions."""
    def __init__(self):
        self.immutable = {}
        self.success = {}
        self.latest = {}
        self.checks = 0

    def check(self, authority):
        snapshot = authority.snapshot()
        records = {}
        for name, blob in snapshot.items():
            if name.startswith("promotion/candidates/") or (
                name.startswith("runs/") and name[5:9].isdigit()
            ):
                sha = digest(blob.body)
                if name in self.immutable and self.immutable[name] != sha:
                    raise Violation("immutable-overwrite", name)
                self.immutable[name] = sha
            try:
                records[name] = json.loads(blob.body)
            except (ValueError, UnicodeDecodeError):
                continue  # malformed seeded records are rejection tests, not commits by production.
        effects = {}
        for effect in authority.effects():
            # The store's effects API is an opaque, durable append log.
            identity = effect.get("identity", effect) if isinstance(effect, dict) else effect
            if isinstance(identity, dict):
                key = (identity.get("job"), identity.get("case"))
            else:
                key = str(identity)
            effects[key] = effects.get(key, 0) + 1
            if effects[key] > 1:
                raise Violation("provider-repeated", str(key))
        phases = {}
        for name, value in records.items():
            if not isinstance(value, dict):
                continue
            if name.startswith("runs/executions/"):
                evidence = value.get("evidenceBlobName")
                if evidence:
                    if evidence not in records:
                        raise Violation("phase-without-evidence", name)
                    phases[evidence] = value
                if value.get("candidatesPublished") and not evidence:
                    raise Violation("candidate-without-evidence", name)
                if value.get("indexPublished") and not value.get("candidatesPublished"):
                    raise Violation("index-without-candidate", name)
                if value.get("digestPublished") and not value.get("indexPublished"):
                    raise Violation("digest-without-index", name)
            if name.startswith("runs/latest/"):
                order = (timestamp_order(value["storedAtUtc"]), value["blobName"])
                if name in self.latest and order < self.latest[name]:
                    raise Violation("latest-regressed", name)
                self.latest[name] = order
                if value["blobName"] not in records:
                    raise Violation("latest-without-archive", name)
                if records[value["blobName"]] != value:
                    raise Violation("latest-evidence-mismatch", name)
        for name, value in records.items():
            if not name.startswith("runs/jobs/") or not isinstance(value, dict):
                continue
            for (job, case), evidence in self.success.items():
                if job == value["jobId"] and not any(r["caseId"] == case for r in value.get("receipts", [])):
                    raise Violation("successful-receipt-removed", case)
            for receipt in value.get("receipts", []):
                key = (value["jobId"], receipt["caseId"])
                if key in self.success and not receipt["ok"]:
                    raise Violation("successful-receipt-downgraded", str(key))
                if receipt["ok"]:
                    if key in self.success and receipt["blobName"] != self.success[key]:
                        raise Violation("successful-receipt-conflict", str(key))
                    self.success[key] = receipt["blobName"]
                    if receipt["blobName"] not in records:
                        raise Violation("receipt-without-archive", str(key))
            if value.get("status") in ("completed", "completed-with-errors"):
                for case in value["caseIds"]:
                    matching = [r for r in value.get("receipts", []) if r["caseId"] == case]
                    if not matching or case not in (value.get("publishedCaseIds") or []):
                        raise Violation("completion-without-receipt-publication", case)
                    phase = phases.get(matching[0]["blobName"], {})
                    if not phase.get("digestPublished"):
                        raise Violation("completion-before-phases", case)
        self.checks += 1


class Child:
    def __init__(self, name, command, spec):
        self.name, self.spec = name, spec
        self.buffer = b""
        self.request = self.response = self.result = None
        self.stage = "waiting"
        self.killed = self.closed = False
        require_no_ambient_identity()
        env = dict(os.environ)
        if any(env.get(k) for k in ("AZURE_CLIENT_ID", "AZURE_TENANT_ID", "AZURE_CLIENT_SECRET",
                "AZURE_CLIENT_CERTIFICATE_PATH", "AZURE_FEDERATED_TOKEN_FILE", "MSI_ENDPOINT", "IDENTITY_ENDPOINT",
                "PK_TEST_STORAGE_CONNECTION")):
            raise RuntimeError("Ambient cloud/emulator settings forbidden")
        self.p = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                  stderr=subprocess.PIPE, env=env, start_new_session=True)
        try:
            os.set_blocking(self.p.stderr.fileno(), False)
            self.p.stdin.write((json.dumps(spec) + "\n").encode())
            self.p.stdin.flush()
        except BaseException:
            # Session.start cannot register a child whose constructor failed.
            # Reap it here even when stdin closed before the spec was written.
            try:
                self.close()
            except Exception:
                pass
            raise

    def receive(self):
        data = os.read(self.p.stdout.fileno(), 65536)
        if not data:
            if self.result is None and not self.killed:
                try:
                    error = os.read(self.p.stderr.fileno(), 1500).decode(errors="replace")
                except BlockingIOError:
                    error = ""
                raise RuntimeError(f"worker {self.name} ended without result: " + error)
            return
        self.buffer += data
        if b"\n" not in self.buffer:
            return
        line, self.buffer = self.buffer.split(b"\n", 1)
        item = json.loads(line)
        if item["kind"] == "result":
            self.result, self.stage = item, "done"
        else:
            self.request, self.stage = item, "request"

    def reply(self):
        self.p.stdin.write((json.dumps(self.response) + "\n").encode())
        self.p.stdin.flush()
        self.request = self.response = None
        self.stage = "waiting"

    def kill(self):
        try:
            # Each child starts a new session. Kill its whole owned group so an
            # unexpected descendant cannot retain pipes or outlive the run.
            os.killpg(self.p.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        self.p.wait(timeout=5)
        self.killed, self.stage = True, "done"
        self.result = {"kind": "result", "ok": False, "signal": 9}

    def close(self):
        if self.closed:
            return
        try:
            if not self.killed:
                try:
                    os.killpg(self.p.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
            self.p.wait(timeout=5)
        finally:
            self.closed = True
            for stream in (self.p.stdin, self.p.stdout, self.p.stderr):
                stream.close()


class Session:
    def __init__(self, command, state, provider_response, seed=0, policy="round-robin", fault=None,
                 prefix=(), check=True, model_check=True):
        self.command, self.state = command, Path(state)
        self.authority = Authority(self.state / "authority.sqlite", page_size=1)
        self.provider_response = provider_response
        self.seed, self.policy, self.fault = seed, policy, fault
        self.random = random.Random(seed)
        self.prefix, self.decisions, self.branches = list(prefix), [], []
        self.choice_points = []
        self.children, self.events = [], []
        self.observer = Observer()
        self.model = TraceAdapter()
        self.check = check
        self.model_check = model_check
        self.fired, self.steps, self.last = 0, 0, -1
        self.provider_pending = set()
        self.deadline = time.monotonic() + 90

    def start(self, spec, name=None):
        child_name = name or f"w{len(self.children)}"
        spec = dict(spec, fixtureDirectory=str(self.state / ("fixture-" + child_name)))
        c = Child(child_name, self.command, spec)
        self.children.append(c)
        return c

    def event(self, child, stage, request=None, **extra):
        item = {"step": len(self.events), "worker": child.name, "event": stage, **extra}
        if request:
            item.update(kind=request["kind"], method=request["method"], object=request_name(request),
                        boundary=boundary(request), request=request["id"])
            headers = {k.lower(): v for k, v in request.get("headers", {}).items()}
            item.update({k: headers[k] for k in ("if-match", "if-none-match") if k in headers})
            value = body_json(request)
            item.update({k: value[k] for k in ("phase", "candidatesPublished", "indexPublished", "digestPublished", "status") if k in value})
        self.events.append(item)

    def await_boundaries(self):
        # Wait for every runnable child to reach a visible boundary before choosing.
        # This removes wall-clock race choices from the deterministic schedule.
        while waiting := [c for c in self.children if c.stage == "waiting"]:
            if time.monotonic() > self.deadline:
                raise TimeoutError("bounded worker deadline")
            ready, _, _ = select.select([c.p.stdout for c in waiting], [], [], 1)
            for c in waiting:
                if c.p.stdout in ready:
                    c.receive()
                    if c.stage == "request":
                        self.event(c, "request", c.request)
                    elif c.stage == "done":
                        self.event(c, "return", ok=c.result.get("ok"), errorType=c.result.get("errorType"))

    def matches_fault(self, child):
        f = self.fault
        if not f or self.fired or child.name != f.get("worker", "w0"):
            return False
        if child.stage != f.get("stage", "response") or boundary(child.request) != f["boundary"]:
            return False
        return True

    def response_for(self, c):
        r = c.request
        if r["kind"] in ("storage", "queue", "http"):
            return self.authority.handle(r)
        if r["kind"] == "source":
            return {"id": r["id"], "status": 200, "headers": {"Content-Type": "text/plain"},
                    "body": base64.b64encode(b"source checked; public synthetic fixture").decode()}
        if r["kind"] == "provider":
            m = c.spec["message"]
            identity = {"job": m["jobId"], "case": m.get("caseId") or m["caseIds"][0]}
            self.authority.effect(identity)
            return {"id": r["id"], "status": 200, "headers": {"Content-Type": "application/json"},
                    "body": base64.b64encode(json.dumps(self.provider_response).encode()).decode()}
        raise RuntimeError("Unexpected IPC kind " + r["kind"])

    def advance(self, c):
        self.steps += 1
        if self.steps > 3500:
            raise TimeoutError("bounded 3500 scheduler actions")
        if self.matches_fault(c):
            self.fired += 1
            kind = self.fault.get("action", "kill")
            self.event(c, kind, c.request, committed=c.stage == "response")
            if kind == "kill":
                if self.model_check:
                    self.model.crash(c.name)
                c.kill()
                return
            if kind in ("lost-response", "cancel"):
                c.response = {"id": c.request["id"], "error": "cancel" if kind == "cancel" else "lost-response"}
                c.reply()
                return
        if c.stage == "request":
            c.response = self.response_for(c)
            c.stage = "response"
            self.event(c, "linearize", c.request, status=c.response.get("status"),
                       etag=c.response.get("headers", {}).get("ETag"))
            if self.check:
                self.observer.check(self.authority)
            if self.check and self.model_check:
                self.model.linearize(c.name, c.spec, c.request, c.response)
        else:
            self.event(c, "deliver", c.request, status=c.response.get("status"))
            if self.check and self.model_check:
                self.model.deliver(c.name, c.spec, c.request, c.response)
            c.reply()

    def run(self):
        while True:
            self.await_boundaries()
            active = [c for c in self.children if c.stage in ("request", "response")]
            if not active:
                return [c.result for c in self.children]
            choices = [self.children.index(c) for c in active]
            self.choice_points.append([{ "worker": self.children.index(c), "stage": c.stage,
                "object": request_name(c.request), "method": c.request["method"],
                "kind": c.request["kind"], "boundary": boundary(c.request)} for c in active])
            n = len(self.decisions)
            if n < len(self.prefix) and self.prefix[n] in choices:
                selected = self.prefix[n]
            elif self.policy == "random":
                selected = self.random.choice(choices)
            elif self.policy == "serial":
                selected = choices[0]
            elif self.policy == "reverse":
                selected = choices[-1]
            else:
                selected = next((x for x in choices if x > self.last), choices[0])
            self.last = selected
            self.branches.append(choices)
            self.decisions.append(selected)
            self.advance(self.children[selected])

    def single(self, spec, name=None):
        child = self.start(spec, name)
        self.run()
        if not child.result.get("ok"):
            raise RuntimeError("Expected successful fixture operation: " + str(child.result))
        return child.result.get("result")

    def close(self):
        errors = []
        for c in self.children:
            try:
                c.close()
            except BaseException as error:
                errors.append(error)
        try:
            self.authority.close()
        except BaseException as error:
            errors.append(error)
        if errors:
            raise errors[0]


def get_description(command, options=None):
    child = Child("describe", command, {"operation": "describe", **(options or {})})
    try:
        deadline = time.monotonic() + 15
        while child.result is None:
            if time.monotonic() > deadline:
                raise TimeoutError("describe deadline")
            ready, _, _ = select.select([child.p.stdout], [], [], 1)
            if ready:
                child.receive()
            if child.request:
                raise RuntimeError("describe must not perform HTTP")
        if not child.result.get("ok"):
            raise RuntimeError(child.result)
        return child.result["result"]
    finally:
        child.close()


def require(condition, code, detail):
    if not condition:
        raise Violation(code, detail)


def completed(authority, message):
    jobs = [json.loads(b.body) for n, b in authority.snapshot().items() if n.startswith("runs/jobs/")]
    return next((j for j in jobs if j["jobId"] == message["jobId"]), {}).get("status") in ("completed", "completed-with-errors")


def run_scenario(command, description, scenario, seed=0, policy="round-robin", fault=None, prefix=()):
    with tempfile.TemporaryDirectory(prefix="w08-explore-") as directory:
        s = Session(command, directory, description["providerResponse"], seed, policy, fault, prefix)
        m = dict(description["message"])
        cases = description["cases"]
        spec = lambda op, msg=m, **extra: {"operation": op, "message": msg, "cases": cases, **extra}
        failure = None
        try:
            s.single(spec("create"), "setup")
            # Schedule numbering starts with the two workers, excluding setup.
            s.children[0].close()
            s.children.clear()
            s.decisions.clear()
            s.branches.clear()
            s.choice_points.clear()
            s.last = -1
            if scenario in ("live-provider", "live-archive"):
                first = s.start(spec("run"), "w0")
                hold = scenario.removeprefix("live-")
                while True:
                    s.await_boundaries()
                    require(first.stage != "done", "schedule-not-reached", hold)
                    if first.stage == "response" and boundary(first.request) == hold:
                        break
                    s.advance(first)
                fresh = s.start(spec("run"), "w1")
                while fresh.stage != "done":
                    s.await_boundaries()
                    if fresh.stage != "done":
                        s.advance(fresh)
                s.run()
            elif scenario == "distinct":
                require(len(m["caseIds"]) >= 2, "fixture", "distinct requires two cases")
                a = dict(m, caseId=m["caseIds"][0])
                b = dict(m, caseId=m["caseIds"][1])
                s.start(spec("run", a), "w0")
                s.start(spec("run", b), "w1")
            else:
                s.start(spec("run"), "w0")
                s.start(spec("run"), "w1")
            s.run()
            if fault:
                require(s.fired == 1, "schedule-not-reached", str(fault))
            before = len(s.authority.effects())
            # Faults cease; fair fresh delivery bounded to three complete attempts.
            s.fault = None
            if scenario == "fanout":
                children = [json.loads(b) for b in s.authority.queue_bodies()]
                require(len(children) >= 2, "fanout-missing", str(len(children)))
                for i, child in enumerate(children):
                    s.single(spec("run", child), f"child-{i}")
                recovery_messages = [dict(m, caseId=c) for c in m["caseIds"]]
            elif scenario == "distinct":
                recovery_messages = [dict(m, caseId=c) for c in m["caseIds"]]
            else:
                recovery_messages = [m]
            for attempt in range(3):
                for i, msg in enumerate(recovery_messages):
                    s.single(spec("run", msg), f"recovery-{attempt}-{i}")
            s.observer.check(s.authority)
            archives = [n for n in s.authority.snapshot() if n.startswith("runs/") and n[5:9].isdigit()]
            if len(archives) == len(m["caseIds"]):
                require(completed(s.authority, m), "recorded-evidence-stranded", m["jobId"])
                require(len(s.authority.effects()) == before or scenario == "fanout", "provider-during-recovery", m["jobId"])
            # An uncertain admission without archive is intentionally allowed to remain unresolved.
        except Exception as ex:
            failure = {"code": getattr(ex, "code", type(ex).__name__), "detail": str(ex)}
        finally:
            try:
                result = {"scenario": scenario, "seed": seed, "policy": policy, "fault": fault,
                          "failure": failure, "actions": s.steps, "events": s.events,
                          "decisions": s.decisions, "branches": s.branches,
                          "choice_points": s.choice_points,
                          "oracle_checks": s.observer.checks, "faults_fired": s.fired,
                          "model": s.model.report(),
                          "provider_effects": len(s.authority.effects()),
                          "objects": len(s.authority.snapshot())}
            finally:
                s.close()
        return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--worker", type=Path, default=ROOT / "NotaryGeek.PublicKnowledge.Worker.Tests/bin/W08Worker/Debug/net10.0/W08Worker.dll")
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--mode", choices=["smoke", "matrix", "replay"], default="smoke")
    parser.add_argument("--schedule", type=Path)
    args = parser.parse_args()
    command = [args.dotnet, "exec", str(args.worker)]
    binding = source_binding(args.worker)
    description = get_description(command)
    args.out.mkdir(parents=True, exist_ok=True)
    source = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    jobs = []
    if args.mode == "replay":
        if args.schedule is None:
            parser.error("--mode replay requires --schedule")
        schedule = json.loads(args.schedule.read_text())
        jobs = [replay_job(schedule, binding)]
    else:
        for policy in ("serial", "reverse", "round-robin", "random"):
            jobs.append({"scenario": "duplicate", "policy": policy, "seed": 17})
        if args.mode == "matrix":
            jobs.extend({"scenario": name, "policy": "serial"} for name in ("live-provider", "live-archive"))
            for boundary_name in ("admission", "provider", "archive", "latest", "evidence-phase", "receipt",
                                  "candidate", "candidate-phase", "index", "index-phase", "digest", "digest-phase", "completion"):
                for stage in ("request", "response"):
                    jobs.append({"scenario": "duplicate", "policy": "serial", "fault": {"worker": "w0", "boundary": boundary_name, "stage": stage, "action": "kill"}})
                for action in ("lost-response", "cancel"):
                    jobs.append({"scenario": "duplicate", "policy": "serial", "fault": {"worker": "w0", "boundary": boundary_name, "stage": "response", "action": action}})
    results = []
    for index, job in enumerate(jobs):
        result = run_scenario(command, description, **job)
        if args.mode == "replay":
            original = schedule["result"]
            expected_code = (original.get("failure") or {}).get("code")
            actual_code = (result.get("failure") or {}).get("code")
            if result["decisions"] != job["prefix"] or expected_code != actual_code:
                result["failure"] = {"code": "replay-diverged", "detail":
                    "Recorded decisions or failure classification were not reproduced."}
        artifact = {"source_head": source, "starting_head": STARTING_HEAD,
                    "source_binding": binding, **job,
                    "bounds": {"actions": 3500, "deadline_seconds": 90, "fair_recovery_attempts": 3},
                    "result": result}
        (args.out / f"schedule-{index:03d}.json").write_text(json.dumps(artifact, indent=2) + "\n")
        summary = {k: result[k] for k in ("scenario", "policy", "seed", "fault", "failure", "actions", "oracle_checks", "provider_effects")}
        results.append(summary)
        print(json.dumps({"schedule": index, **summary}), flush=True)
    changed = binding != source_binding(args.worker)
    report = {"source_head": source, "source_binding": binding,
              "source_changed_during_run": changed, "schedules": len(results),
              "failed": sum(bool(x["failure"]) for x in results) + int(changed),
              "results": results, "scope": "bounded concrete schedules; no exhaustive distributed correctness claim"}
    (args.out / "summary.json").write_text(json.dumps(report, indent=2) + "\n")
    return bool(report["failed"])


if __name__ == "__main__":
    raise SystemExit(main())
