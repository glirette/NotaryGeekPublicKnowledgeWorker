#!/usr/bin/env python3
"""Translate observable production HTTP boundaries into the independent model.

Session hooks (before the child request/response is cleared):
    adapter.linearize(child.name, child.spec, request, response)
    adapter.deliver(child.name, child.spec, request, response)
    adapter.crash(child.name)

Call linearize exactly once after an authority operation/provider effect. A
provider-kind linearization is already the provider observation; do not emit it
again at response delivery. Failed HTTP writes have no durable event. Only the
original successful reservation's delivered success response confers authority;
reads, 412s, retries, lost responses, and method returns never do.

This module observes artifacts; it neither grants admissions nor makes recovery
decisions. Full histories cover actual ProcessQueuedBatch (operation='run')
identities. Direct save/admin work is separately reported, not assigned invented
provider calls. Externally seeded evidence and non-provider results are excluded
from the provider-bearing history with explicit reasons. An externally observed
unknown reservation can still be imported without authority, to reject a retry.

Derived view completeness is NOT checked by this adapter: it calls the model
with check_views=False. Candidate PUTs and exact reused candidates are observed;
when no write is observed, candidatesPublished records abstract step completion
only. Candidate-set completeness/routing is explicitly outside this adapter's
claim; it does not guess whether drafts should have been promoted. Terminal successful cases produce ack; terminal failed cases are checked
for matching evidence, receipt and digest phase separately (model ack means
success). The runner's snapshot observer remains an independent companion.
"""

from __future__ import annotations

import base64
import hashlib
import json
from collections import Counter
from urllib.parse import unquote, urlparse

from w08_model import _time, validate_history


class ModelTraceViolation(AssertionError):
    def __init__(self, code, detail):
        self.code, self.detail = code, detail
        super().__init__(f"{code}: {detail}")


def _name(request):
    return unquote(urlparse(request.get("url", "")).path).removeprefix("/fixture/")


def _body(item):
    return base64.b64decode(item.get("body") or "", validate=True)


def _json(item):
    value = json.loads(_body(item))
    if not isinstance(value, dict):
        raise ModelTraceViolation("model-adapter-schema", "expected a JSON object artifact")
    return value


def _key(spec, case=None, job=None):
    message = spec.get("message") or {}
    cases = message.get("caseIds") or []
    selected = case or spec.get("caseId") or message.get("caseId") or (cases[0] if len(cases) == 1 else None)
    identity = job or message.get("jobId")
    if not identity or not selected:
        raise ModelTraceViolation("model-adapter-identity", "case operation lacks an explicit job/case identity")
    return identity, selected


class TraceAdapter:
    """In-memory trace observer; retained by a Session across fresh subprocesses."""
    def __init__(self):
        self.history = []
        self.checks = 0
        self.reservations = {}
        self.execution_objects = {}
        self.pending_admissions = {}
        self.archives = {}
        self.evidence_keys = {}
        self.provider_attempted = set()
        self.phases = {}
        self.candidates = set()
        self.candidate_attempts = {}
        self.receipts = {}
        self.acknowledged = set()
        self.excluded = {}
        self.admin_operations = Counter()
        self.observed_operations = Counter()
        self.seeded_unknown_reservations = 0
        self.empty_candidate_sets = 0
        self.marker_only_candidate_steps = 0
        self.failed_completions = 0

    def _check(self):
        errors = validate_history(self.history, check_views=False)
        self.checks += 1
        if errors:
            error = errors[0]
            raise ModelTraceViolation("model-" + error.code,
                                      f"normalized event {error.index}: {error.detail}")

    def _emit(self, op, key=None, **fields):
        event = {"op": op, **fields}
        if key is not None:
            event.update(job=key[0], case=key[1])
        self.history.append(event)
        self._check()
        return len(self.history) - 1

    def _exclude(self, key, reason):
        self.excluded.setdefault(key, reason)

    def _reservation(self, worker, spec, request, value, *, imported=False):
        key = _key(spec)
        name = _name(request)
        previous_key = self.execution_objects.setdefault(name, key)
        if previous_key != key:
            raise ModelTraceViolation("model-execution-object-identity", f"{name}: {previous_key} -> {key}")
        if key in self.excluded:
            return
        phase = value.get("phase")
        if imported and key not in self.reservations and phase == "evidence-recorded":
            self._exclude(key, "evidence existed before the observed ProcessQueuedBatch history")
            return
        fingerprint, submitted = value.get("fingerprint"), value.get("reservedAtUtc")
        if phase == "provider-outcome-unknown":
            if imported and key in self.reservations:
                old = self.reservations[key]
                if old["fingerprint"] != fingerprint or _time(old["submitted_at"]) != _time(submitted):
                    raise ModelTraceViolation("model-admission-binding", str(key))
                return
            index = self._emit("admit", key, worker=worker, fingerprint=fingerprint,
                               submitted_at=submitted, granted=True, acknowledged=False)
            self.reservations[key] = {"fingerprint": fingerprint, "submitted_at": submitted}
            if imported:
                self.seeded_unknown_reservations += 1
            else:
                self.pending_admissions[(worker, request["id"])] = index
            return
        if imported:
            return
        if phase != "evidence-recorded":
            raise ModelTraceViolation("model-invalid-persisted-phase", str(phase))
        bound = self.reservations.get(key)
        if bound is None:
            raise ModelTraceViolation("model-phase-without-admission", str(key))
        if bound["fingerprint"] != fingerprint or _time(bound["submitted_at"]) != _time(submitted):
            raise ModelTraceViolation("model-admission-binding", str(key))
        archived = self.archives.get(key)
        if archived is None or archived["evidence_id"] != value.get("evidenceBlobName"):
            raise ModelTraceViolation("model-phase-evidence-identity", str(key))
        flags = [True, bool(value.get("candidatesPublished")), bool(value.get("indexPublished")), bool(value.get("digestPublished"))]
        for index in range(1, 4):
            if flags[index] and not flags[index - 1]:
                raise ModelTraceViolation("model-phase-order", str(key))
        target = max(index + 1 for index, flag in enumerate(flags) if flag)
        prior = self.phases.get(key, 0)
        if target < prior or target > prior + 1:
            raise ModelTraceViolation("model-phase-order", f"{key}: {prior} -> {target}")
        if target == 2 and key not in self.candidates:
            result = archived["result"]
            drafts = (result.get("structuredOutput") or {}).get("candidates") or []
            if not result.get("ok") or not drafts:
                self.empty_candidate_sets += 1
            self.marker_only_candidate_steps += 1
            self._candidate(key, source="publication-marker-only; candidate-set-completeness-unchecked")
        if target > prior:
            self._emit("phase", key, phase=("evidence", "candidates", "index", "digest")[target - 1])
            self.phases[key] = target

    def _candidate(self, key, source):
        if key in self.candidates or key in self.excluded:
            return
        archived = self.archives.get(key)
        if archived is None:
            raise ModelTraceViolation("model-candidate-without-archive", str(key))
        self._emit("candidate", key, evidence_id=archived["evidence_id"], observation_source=source)
        self.candidates.add(key)

    def _archive(self, spec, request, value):
        key = _key(spec, case=value.get("caseId"))
        if key in self.excluded:
            return
        result = value.get("result") or {}
        if key not in self.provider_attempted and not (result.get("providerCalled") or result.get("openAiCalled")):
            self._exclude(key, "recorded non-provider result; outside provider-bearing model scope")
            return
        bound = self.reservations.get(key)
        if bound is None:
            raise ModelTraceViolation("model-archive-without-admission", str(key))
        eid = value.get("blobName")
        if eid != _name(request):
            raise ModelTraceViolation("model-archive-object-identity", str(key))
        payload = hashlib.sha256(_body(request)).hexdigest()
        self._emit("archive", key, evidence_id=eid, payload=payload,
                   fingerprint=bound["fingerprint"], submitted_at=value.get("storedAtUtc"))
        self.archives[key] = {"evidence_id": eid, "result": result, "envelope": value,
                              "latest_object": value.get("latestBlobName")}
        self.evidence_keys[eid] = key

    def _job(self, spec, value):
        job = value.get("jobId")
        message = spec.get("message") or {}
        if job != message.get("jobId") or value.get("caseIds") != message.get("caseIds"):
            raise ModelTraceViolation("model-job-envelope-identity", str(job))
        all_receipts = value.get("receipts") or []
        current = {(job, item.get("caseId")): item for item in all_receipts}
        if len(current) != len(all_receipts):
            raise ModelTraceViolation("model-duplicate-receipt-identity", str(job))
        for key, previous in self.receipts.items():
            if key[0] == job and previous[0] == "succeeded" and key not in current:
                raise ModelTraceViolation("model-successful-receipt-omitted", str(key))
        for key, item in current.items():
            if key in self.excluded or key not in self.reservations:
                continue
            status = "succeeded" if item.get("ok") else "failed"
            normalized = status, item.get("blobName")
            if self.receipts.get(key) != normalized:
                self._emit("receipt", key, status=status, evidence_id=normalized[1])
                self.receipts[key] = normalized
        if value.get("status") not in {"completed", "completed-with-errors"}:
            return
        published = set(value.get("publishedCaseIds") or [])
        for case in value.get("caseIds") or []:
            key = job, case
            if key in self.excluded or key not in self.reservations:
                continue
            receipt, archive = self.receipts.get(key), self.archives.get(key)
            if not receipt or not archive or receipt[1] != archive["evidence_id"] or case not in published or self.phases.get(key) != 4:
                raise ModelTraceViolation("model-completion-evidence-publication", str(key))
            if key not in self.acknowledged:
                if receipt[0] == "succeeded":
                    self._emit("ack", key)
                else:
                    self.failed_completions += 1
                self.acknowledged.add(key)

    def linearize(self, worker, spec, request, reply):
        """Observe an HTTP/effect linearization, whether or not reply is delivered."""
        operation = spec.get("operation")
        if operation != "run":
            self.admin_operations[str(operation)] += 1
            return
        self.observed_operations[request.get("kind", "unknown")] += 1
        kind, method, name = request.get("kind"), request.get("method"), _name(request)
        if kind == "provider":
            key = _key(spec)
            if key in self.excluded:
                raise ModelTraceViolation("model-provider-after-preexisting-evidence", str(key))
            self._emit("provider", key, worker=worker)
            self.provider_attempted.add(key)
            return
        if kind != "storage":
            return
        status = reply.get("status", 0)
        if method == "PUT" and name.startswith("promotion/candidates/"):
            self.candidate_attempts[(worker, name)] = _body(request)
        if not 200 <= status < 300:
            return
        if method == "GET":
            if name.startswith("runs/executions/"):
                self._reservation(worker, spec, request, _json(reply), imported=True)
            elif name.startswith("promotion/candidates/") and (worker, name) in self.candidate_attempts:
                attempted = json.loads(self.candidate_attempts[(worker, name)])
                if attempted == _json(reply):
                    self._candidate(_key(spec), source="matching-reused-candidate")
            return
        if method != "PUT":
            return
        if name.startswith("runs/executions/"):
            self._reservation(worker, spec, request, _json(request))
        elif name.startswith("runs/jobs/"):
            self._job(spec, _json(request))
        elif name.startswith("promotion/candidates/"):
            self._candidate(_key(spec), source="committed-candidate-put")
        elif name.startswith("runs/latest/"):
            value = _json(request)
            key = self.evidence_keys.get(value.get("blobName"))
            if key is None:
                candidate = _key(spec, case=value.get("caseId"))
                if candidate in self.excluded:
                    return
                raise ModelTraceViolation("model-latest-without-observed-archive", str(candidate))
            archive = self.archives[key]
            if name != archive["latest_object"] or value.get("caseId") != key[1] or value != archive["envelope"]:
                raise ModelTraceViolation("model-latest-artifact-identity", str(key))
            self._emit("latest", key, evidence_id=value.get("blobName"), submitted_at=value.get("storedAtUtc"))
        elif name.startswith("runs/") and name[5:9].isdigit():
            self._archive(spec, request, _json(request))

    def deliver(self, worker, spec, request, reply):
        """Only a successful reservation response grants local provider authority."""
        index = self.pending_admissions.pop((worker, request["id"]), None)
        if index is not None and 200 <= reply.get("status", 0) < 300 and not reply.get("error"):
            self.history[index]["acknowledged"] = True
            self._check()

    def crash(self, worker):
        self.pending_admissions = {key: index for key, index in self.pending_admissions.items() if key[0] != worker}
        self._emit("crash", worker=worker)

    def report(self):
        return {
            "oracle": "w08_model.validate_history",
            "check_views": False,
            "derived_view_completeness_checked": False,
            "candidate_set_completeness_checked": False,
            "checks": self.checks,
            "normalized_events": len(self.history),
            "history": self.history,
            "fully_observed_run_identities": [list(key) for key in sorted(self.reservations) if key not in self.excluded],
            "excluded_identities": [{"job": key[0], "case": key[1], "reason": reason} for key, reason in sorted(self.excluded.items())],
            "admin_scope_boundaries": dict(sorted(self.admin_operations.items())),
            "run_scope_boundaries": dict(sorted(self.observed_operations.items())),
            "seeded_unknown_reservations_without_authority": self.seeded_unknown_reservations,
            "empty_or_failed_candidate_sets": self.empty_candidate_sets,
            "candidate_steps_known_only_from_marker": self.marker_only_candidate_steps,
            "failed_terminal_cases_checked_separately": self.failed_completions,
            "fingerprint_scope": "durable reservation identity and phase immutability; archive has no independent fingerprint field",
        }


def _self_test():
    """Socket-free adapter checks usable before a production worker is available."""
    import unittest

    class AdapterTests(unittest.TestCase):
        timestamp = "2026-09-26T12:00:00.0000001Z"
        spec = {"operation": "run", "message": {"jobId": "j0", "caseId": "a", "caseIds": ["a"], "submittedAtUtc": timestamp}}

        def request(self, name, value, number=1, method="PUT"):
            return {"id": number, "kind": "storage", "method": method, "url": "https://storage.invalid/fixture/" + name,
                    "body": base64.b64encode(json.dumps(value).encode()).decode()}

        def admission(self, adapter, *, delivered=True):
            request = self.request("runs/executions/j/a.json", {
                "fingerprint": "fp", "phase": "provider-outcome-unknown", "reservedAtUtc": self.timestamp})
            adapter.linearize("w0", self.spec, request, {"status": 201})
            if delivered:
                adapter.deliver("w0", self.spec, request, {"status": 201})

        def archive(self, adapter, *, ok=True):
            adapter.linearize("w0", self.spec, {"kind": "provider"}, {"status": 200})
            request = self.request("runs/2026/a.json", {"blobName": "runs/2026/a.json", "caseId": "a",
                "latestBlobName": "runs/latest/a.json",
                "storedAtUtc": self.timestamp, "result": {"openAiCalled": True, "ok": ok, "structuredOutput": {"candidates": []}}}, 2)
            adapter.linearize("w0", self.spec, request, {"status": 201})
            return request

        def test_admission_response_loss_has_no_provider_authority(self):
            adapter = TraceAdapter()
            self.admission(adapter, delivered=False)
            with self.assertRaisesRegex(ModelTraceViolation, "provider_authorized"):
                adapter.linearize("w0", self.spec, {"kind": "provider"}, {"status": 200})

        def test_error_response_and_crash_cannot_acknowledge_reservation(self):
            for crash in (False, True):
                adapter = TraceAdapter()
                self.admission(adapter, delivered=False)
                if crash:
                    adapter.crash("w0")
                else:
                    adapter.deliver("w0", self.spec, {"id": 1}, {"error": "lost-response"})
                self.assertFalse(adapter.history[0]["acknowledged"])
                with self.assertRaisesRegex(ModelTraceViolation, "provider_authorized"):
                    adapter.linearize("w0", self.spec, {"kind": "provider"}, {"status": 200})

        def test_imported_unknown_reservation_cannot_authorize_provider(self):
            adapter = TraceAdapter()
            stored = self.request("runs/executions/j/a.json", {
                "fingerprint": "fp", "phase": "provider-outcome-unknown", "reservedAtUtc": self.timestamp})
            request = self.request("runs/executions/j/a.json", {}, method="GET")
            adapter.linearize("w1", self.spec, request, {"status": 200, "body": stored["body"]})
            adapter.deliver("w1", self.spec, request, {"status": 200})
            with self.assertRaisesRegex(ModelTraceViolation, "provider_authorized"):
                adapter.linearize("w1", self.spec, {"kind": "provider"}, {"status": 200})

        def test_delivered_admission_records_archive_and_immutable_bytes(self):
            adapter = TraceAdapter()
            self.admission(adapter)
            request = self.archive(adapter)
            adapter.linearize("w0", self.spec, request, {"status": 412})
            self.assertEqual(3, len(adapter.history))
            adapter.linearize("w0", self.spec, request, {"status": 201})
            self.assertEqual(4, len(adapter.history))
            body = _json(request)
            body["unexpected"] = "changed"
            with self.assertRaisesRegex(ModelTraceViolation, "archive_immutable"):
                adapter.linearize("w0", self.spec, self.request("runs/2026/a.json", body, 3), {"status": 201})

        def test_timestamp_preserves_the_seventh_digit_and_timezone(self):
            self.assertEqual(1, _time("2026-09-26T12:00:00.0000002Z") - _time(self.timestamp))
            self.assertEqual(_time(self.timestamp), _time("2026-09-26T13:00:00.0000001+01:00"))

        def test_admin_calls_are_separate(self):
            adapter = TraceAdapter()
            request = self.request("runs/2026/a.json", {}, 3)
            adapter.linearize("admin", {**self.spec, "operation": "save"}, request, {"status": 201})
            self.assertEqual([], adapter.history)
            self.assertEqual({"save": 1}, adapter.report()["admin_scope_boundaries"])

        def test_latest_object_cannot_alias_another_case(self):
            adapter = TraceAdapter()
            self.admission(adapter)
            archive = self.archive(adapter)
            with self.assertRaisesRegex(ModelTraceViolation, "latest-artifact-identity"):
                adapter.linearize("w0", self.spec, self.request("runs/latest/b.json", _json(archive)), {"status": 201})

        def test_failed_provider_result_can_complete_all_phases(self):
            adapter = TraceAdapter()
            self.admission(adapter)
            self.archive(adapter, ok=False)
            execution = {"fingerprint": "fp", "reservedAtUtc": self.timestamp, "phase": "evidence-recorded",
                         "evidenceBlobName": "runs/2026/a.json"}
            for step in (None, "candidatesPublished", "indexPublished", "digestPublished"):
                if step:
                    execution[step] = True
                adapter.linearize("w0", self.spec, self.request("runs/executions/j/a.json", execution), {"status": 201})
            job = {"jobId": "j0", "caseIds": ["a"], "status": "completed-with-errors", "publishedCaseIds": ["a"],
                   "receipts": [{"caseId": "a", "ok": False, "blobName": "runs/2026/a.json"}]}
            adapter.linearize("w0", self.spec, self.request("runs/jobs/j0.json", job), {"status": 201})
            self.assertEqual(1, adapter.report()["failed_terminal_cases_checked_separately"])

        def test_merged_receipts_cover_every_case_and_cannot_drop_success(self):
            adapter = TraceAdapter()
            specs = {}
            for case in ("a", "b"):
                spec = {"operation": "run", "message": {"jobId": "j0", "caseId": case, "caseIds": ["a", "b"], "submittedAtUtc": self.timestamp}}
                specs[case] = spec
                worker = "worker-" + case
                execution = {"fingerprint": "fp-" + case, "phase": "provider-outcome-unknown", "reservedAtUtc": self.timestamp}
                admission = self.request("runs/executions/j/" + case + ".json", execution)
                adapter.linearize(worker, spec, admission, {"status": 201})
                adapter.deliver(worker, spec, admission, {"status": 201})
                adapter.linearize(worker, spec, {"kind": "provider"}, {"status": 200})
                evidence = "runs/2026/" + case + ".json"
                archive = {"caseId": case, "blobName": evidence, "latestBlobName": "runs/latest/" + case + ".json",
                           "storedAtUtc": self.timestamp, "result": {"openAiCalled": True, "ok": True}}
                adapter.linearize(worker, spec, self.request(evidence, archive), {"status": 201})
                execution.update(phase="evidence-recorded", evidenceBlobName=evidence)
                adapter.linearize(worker, spec, self.request("runs/executions/j/" + case + ".json", execution), {"status": 201})
            job = {"jobId": "j0", "caseIds": ["a", "b"], "status": "publishing", "receipts": [
                {"caseId": case, "ok": True, "blobName": "runs/2026/" + case + ".json"} for case in ("a", "b")]}
            adapter.linearize("worker-b", specs["b"], self.request("runs/jobs/j0.json", job), {"status": 201})
            self.assertEqual({"a", "b"}, {event["case"] for event in adapter.history if event["op"] == "receipt"})
            job["receipts"] = job["receipts"][1:]
            with self.assertRaisesRegex(ModelTraceViolation, "successful-receipt-omitted"):
                adapter.linearize("worker-b", specs["b"], self.request("runs/jobs/j0.json", job), {"status": 201})

    result = unittest.TextTestRunner(verbosity=1).run(unittest.defaultTestLoader.loadTestsFromTestCase(AdapterTests))
    return 0 if result.wasSuccessful() else 1


if __name__ == "__main__":
    raise SystemExit(_self_test())
