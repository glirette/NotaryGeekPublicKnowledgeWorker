#!/usr/bin/env python3
"""Scripted SDK schedules for fan-out, pointer ordering and derived publication.

Every worker operation uses the production C# service through the installed SDK.
Only malformed-record prerequisites are seeded directly. The authority remains
an opaque byte/ETag store; all case and publication assertions live here.
"""
from __future__ import annotations

import argparse
import base64
from copy import deepcopy
from datetime import datetime, timedelta, timezone
import json
from pathlib import Path
import re
import subprocess
import tempfile
from urllib.parse import parse_qs, urlparse

from w08_explore import (ROOT, Session, boundary, digest, get_description, require,
                         request_name, source_binding)


def spec(description, operation, message=None, **extra):
    return {"operation": operation, "message": message or description["message"],
            "cases": description["cases"], **extra}


def reach(session, child, predicate):
    """Pump this child only, leaving every other visible boundary held."""
    while True:
        session.await_boundaries()
        require(child.stage != "done", "schedule-not-reached",
                f"{child.name} returned before its selected boundary: {child.result}")
        if predicate(child):
            return
        session.advance(child)


def finish(session, child, success=True):
    while child.stage != "done":
        session.await_boundaries()
        if child.stage != "done":
            session.advance(child)
    require(bool(child.result.get("ok")) == success, "unexpected-operation-result",
            f"{child.name}: {child.result}")
    return child.result.get("result")


def invoke(session, description, operation, name, message=None, success=True, **extra):
    child = session.start(spec(description, operation, message, **extra), name)
    return finish(session, child, success)


def at_write(child, name, stage="request"):
    return child.stage == stage and child.request["method"] == "PUT" and request_name(child.request) == name


def document(session, name):
    record = session.authority.get(name)
    require(record is not None, "missing-artifact", name)
    return json.loads(record.body)


def queued(session, message):
    return document(session, "runs/jobs/" + message["jobId"].lower() + ".json")


def view_name(operation):
    return "runs/latest-index.json" if operation == "index" else "runs/latest-needs-greg.json"


def references(value, operation):
    return sorted((item["caseId"], item["storedAtUtc"], item["blobName"])
                  for item in value["items" if operation == "index" else "sourceRuns"])


def assert_current_view(session, operation, returned=None):
    latest = [json.loads(blob.body) for name, blob in session.authority.snapshot().items()
              if name.startswith("runs/latest/")]
    wanted = sorted((item["caseId"], item["storedAtUtc"], item["blobName"]) for item in latest)
    actual = document(session, view_name(operation))
    require(references(actual, operation) == wanted, "derived-view-stale-after-quiescence", operation)
    if returned is not None:
        require(references(returned, operation) == wanted, "returned-view-stale-after-quiescence", operation)


def later_message(description, number):
    origin = datetime.fromisoformat(description["message"]["submittedAtUtc"].replace("Z", "+00:00"))
    return dict(description["message"], jobId=f"job-extended-save-{number}",
                submittedAtUtc=(origin + timedelta(minutes=number)).astimezone(timezone.utc)
                .isoformat(timespec="seconds").replace("+00:00", "Z"))


def direct_save(session, description, number, name=None):
    return invoke(session, description, "save", name or f"save-{number}", later_message(description, number))


def latest_conditions(session, worker):
    return [event for event in session.events if event["worker"] == worker and
            event["event"] == "linearize" and event.get("boundary") == "latest"]


def case_independence(session, description, details):
    message = description["message"]
    invoke(session, description, "create", "create")
    children = [session.start(spec(description, "run", dict(message, caseId=case)), f"case-{index}")
                for index, case in enumerate(message["caseIds"])]
    session.run()
    for child in children:
        require(child.result.get("ok"), "independent-case-failed", child.name)
    for index, case in enumerate(message["caseIds"]):
        invoke(session, description, "run", f"replay-{index}", dict(message, caseId=case))
    final = queued(session, message)
    require(final["status"] == "completed" and len(final["receipts"]) == 2,
            "independent-cases-not-completed", str(final["status"]))
    require(len(session.authority.effects()) == 2, "independent-case-effect-count", "expected one effect per case")
    details.update(independent_cases=2, receipts=2, completed=True)


def fanout(session, description, details, stage):
    message = description["message"]
    invoke(session, description, "create", "create")
    parent = session.start(spec(description, "run"), "interrupted-parent")
    reach(session, parent, lambda child: child.stage == stage and boundary(child.request) == "enqueue")
    session.event(parent, "kill", parent.request, committed=stage == "response")
    session.model.crash(parent.name)
    parent.kill()
    expected = int(stage == "response")
    require(len(session.authority.queue_bodies()) == expected, "enqueue-commit-boundary", stage)
    invoke(session, description, "run", "parent-recovery")
    invoke(session, description, "run", "parent-duplicate")
    messages = [json.loads(body) for body in session.authority.queue_bodies()]
    require(len(messages) == expected + 4, "fanout-replay-count", str(len(messages)))
    children = [session.start(spec(description, "run", child), f"delivery-{index}")
                for index, child in enumerate(messages)]
    session.run()
    for child in children:
        require(child.result.get("ok"), "fanout-child-failed", child.name)
    for index, case in enumerate(message["caseIds"]):
        invoke(session, description, "run", f"fair-replay-{index}", dict(message, caseId=case))
    final = queued(session, message)
    require(final["status"] == "completed" and len(final["receipts"]) == 2,
            "fanout-evidence-stranded", str(final["status"]))
    require(len(session.authority.effects()) == 2, "fanout-provider-count", "duplicated children must reuse evidence")
    details.update(legacy=message.get("caseFingerprints") is None, kill_stage=stage,
                   child_messages=len(messages), effects_after_duplicate_children=2)


def save_order(session, description, details, mode, command):
    if mode == "stale-etag":
        direct_save(session, description, 0, "initial-save")
    low_message, high_message = later_message(description, 1), later_message(description, 2)
    if mode == "equal-time":
        high_message["submittedAtUtc"] = low_message["submittedAtUtc"]
        variants = [get_description(command, {"message": value, "cases": description["cases"]})
                    for value in (low_message, high_message)]
        variants.sort(key=lambda value: value["names"]["evidence"])
        low_message, high_message = (value["message"] for value in variants)
    latest = description["names"]["latest"]
    low = session.start(spec(description, "save", low_message), "lower-save")
    high = session.start(spec(description, "save", high_message), "higher-save")
    reach(session, low, lambda child: at_write(child, latest))
    reach(session, high, lambda child: at_write(child, latest))
    low_etag = {k.lower(): v for k, v in low.request["headers"].items()}
    high_etag = {k.lower(): v for k, v in high.request["headers"].items()}
    header = "if-match" if mode == "stale-etag" else "if-none-match"
    require(low_etag.get(header) == high_etag.get(header) and header in low_etag,
            "cas-prerequisite", f"both writers must observe the same {header}")
    higher_receipt = finish(session, high)
    lower_receipt = finish(session, low)
    require(document(session, latest)["blobName"] == higher_receipt["blobName"],
            "latest-ordering-regressed", mode)
    require(any(event["status"] == 412 for event in latest_conditions(session, "lower-save")),
            "stale-write-not-rejected", mode)
    for receipt in (higher_receipt, lower_receipt):
        require(session.authority.get(receipt["blobName"]) is not None, "lost-immutable-archive", mode)
    details.update(ordering=mode, losing_write_status=412, winner=higher_receipt["blobName"],
                   archives_preserved=True)


def view_advance(session, description, details, operation):
    direct_save(session, description, 0)
    publisher = session.start(spec(description, operation), "publisher")
    reach(session, publisher, lambda child: at_write(child, view_name(operation), "response"))
    old_view = session.authority.get(view_name(operation)).body
    newer = direct_save(session, description, 1)
    require(newer["blobName"] not in str(json.loads(old_view)), "advance-prerequisite", operation)
    returned = finish(session, publisher)
    assert_current_view(session, operation, returned)
    writes = [event for event in session.events if event["worker"] == "publisher" and
              event["event"] == "linearize" and event.get("boundary") == operation and event.get("status") == 201]
    require(len(writes) == 2, "derived-rebuild-not-observed", operation)
    details.update(view=operation, pointer_advance="after-derived-commit-before-reply", successful_view_writes=2)


def view_stale_etag(session, description, details, operation):
    direct_save(session, description, 0)
    invoke(session, description, operation, "initial-view")
    direct_save(session, description, 1)
    stale = session.start(spec(description, operation), "stale-publisher")
    reach(session, stale, lambda child: at_write(child, view_name(operation)))
    require(any(key.lower() == "if-match" for key in stale.request["headers"]),
            "derived-cas-prerequisite", operation)
    direct_save(session, description, 2)
    invoke(session, description, operation, "newer-publisher")
    returned = finish(session, stale)
    assert_current_view(session, operation, returned)
    require(any(event["worker"] == "stale-publisher" and event["event"] == "linearize" and
                event.get("boundary") == operation and event.get("status") == 412 for event in session.events),
            "derived-stale-etag-not-rejected", operation)
    details.update(view=operation, stale_matching_etag_status=412, recovered=True)


def view_bound(session, description, details, operation):
    direct_save(session, description, 0)
    publisher = session.start(spec(description, operation), "contended-publisher")
    for attempt in range(10):
        reach(session, publisher, lambda child: at_write(child, view_name(operation), "response"))
        direct_save(session, description, attempt + 1)
        session.advance(publisher)  # Deliver a committed view after its source advanced.
    finish(session, publisher, success=False)
    require(publisher.result.get("errorType") == "InvalidOperationException", "wrong-rebuild-bound-error", operation)
    writes = [event for event in session.events if event["worker"] == "contended-publisher" and
              event["event"] == "linearize" and event.get("boundary") == operation and event.get("status") == 201]
    require(len(writes) == 10, "unbounded-derived-contention", str(len(writes)))
    require(len(session.authority.effects()) == 0, "provider-in-admin-rebuild", operation)
    returned = invoke(session, description, operation, "settled-publisher")
    assert_current_view(session, operation, returned)
    details.update(view=operation, retries_observed=10, bounded_failure=publisher.result["errorType"],
                   fair_recovery=True)


def view_listing_failure(session, description, details, operation, command):
    direct_save(session, description, 0)
    second_case = [dict(description["cases"][0], id="case-b")]
    alternate = get_description(command, {"cases": second_case, "jobId": "job-second-listed-case"})
    invoke(session, alternate, "save", "second-case-save")
    invoke(session, description, operation, "prior-view")
    prior = session.authority.get(view_name(operation))
    publisher = session.start(spec(description, operation), "page-fault-publisher")

    def second_page(child):
        if child.stage != "request" or child.request["method"] != "GET":
            return False
        query = parse_qs(urlparse(child.request["url"]).query)
        return (query.get("comp") == ["list"] and query.get("restype") == ["container"] and
                query.get("prefix") == ["runs/latest/"] and bool(query.get("marker", [""])[0]))

    reach(session, publisher, second_page)
    publisher.response = {"id": publisher.request["id"], "status": 500,
                          "headers": {"Content-Type": "application/xml", "x-ms-error-code": "SyntheticPageFailure"},
                          "body": base64.b64encode(b"<Error><Code>SyntheticPageFailure</Code><Message>synthetic</Message></Error>").decode()}
    publisher.stage = "response"
    session.event(publisher, "inject-second-page-error", publisher.request, status=500, committed=False)
    finish(session, publisher, success=False)
    require(publisher.result.get("errorType") == "RequestFailedException", "wrong-listing-failure", operation)
    after = session.authority.get(view_name(operation))
    require(after.body == prior.body and after.etag == prior.etag, "partial-list-replaced-view", operation)
    require(not any(event["worker"] == "page-fault-publisher" and event["event"] == "linearize" and
                    event.get("boundary") == operation for event in session.events),
            "partial-list-attempted-derived-write", operation)
    returned = invoke(session, description, operation, "listing-recovery")
    assert_current_view(session, operation, returned)
    details.update(view=operation, failed_page=2, prior_bytes_preserved=True, recovered=True)


def malformed_archive(session, description, details):
    invoke(session, description, "create", "create")
    invoke(session, description, "admit", "reserve")
    receipt = invoke(session, description, "save", "save")
    corrupted = b'{"schema":"notary-geek-public-knowledge-stored-run-v1","result":'
    session.authority.seed(receipt["blobName"], corrupted)
    original_effects = len(session.authority.effects())
    worker = session.start(spec(description, "run"), "malformed-evidence-reader")
    finish(session, worker, success=False)
    require(worker.result.get("errorType") == "JsonException", "wrong-malformed-evidence-rejection", str(worker.result))
    require(session.authority.get(receipt["blobName"]).body == corrupted,
            "malformed-immutable-evidence-overwritten", receipt["blobName"])
    require(len(session.authority.effects()) == original_effects == 0, "provider-after-malformed-evidence", "run")
    require(not queued(session, description["message"])["receipts"], "malformed-evidence-gained-receipt", "run")
    details.update(rejected=worker.result["errorType"], corrupted_bytes_preserved=True, provider_effects=0)


def candidate_conflict(session, description, details):
    invoke(session, description, "create", "create")
    invoke(session, description, "admit", "reserve")
    receipt = invoke(session, description, "save", "save")
    invoke(session, description, "promote", "initial-candidate")
    candidate_names = [name for name in session.authority.snapshot() if name.startswith("promotion/candidates/")]
    require(len(candidate_names) == 1, "candidate-prerequisite", str(candidate_names))
    candidate = candidate_names[0]
    conflict = b'{"syntheticConflict":true}'
    session.authority.seed(candidate, conflict)
    evidence_before = session.authority.get(receipt["blobName"]).body
    for number in range(2):
        worker = session.start(spec(description, "run"), f"conflict-reader-{number}")
        finish(session, worker, success=False)
        require(worker.result.get("errorType") == "InvalidOperationException" and
                "Conflicting immutable promotion candidate" in worker.result.get("error", ""),
                "wrong-candidate-conflict-rejection", str(worker.result))
        require(session.authority.get(candidate).body == conflict, "conflicting-candidate-overwritten", candidate)
        require(session.authority.get(receipt["blobName"]).body == evidence_before,
                "candidate-failure-changed-evidence", receipt["blobName"])
    state = queued(session, description["message"])
    require(state["status"] == "publishing" and len(state["receipts"]) == 1 and state["receipts"][0]["ok"],
            "candidate-conflict-lost-receipt", str(state["status"]))
    require(len(session.authority.effects()) == 0, "provider-after-candidate-conflict", "run")
    details.update(rejected_replays=2, candidate_bytes_preserved=True, evidence_bytes_preserved=True,
                   successful_receipt_preserved=True, publication_pending=True)


NAMES = ["independent-cases"] + [f"{version}-fanout-{stage}" for version in ("legacy", "modern")
                                for stage in ("request", "response")] + [
    "save-older-newer", "save-equal-time", "save-stale-etag"] + [
    f"{operation}-{schedule}" for operation in ("index", "digest")
    for schedule in ("advance-after-commit", "stale-etag", "bounded-rebuild", "partial-list-failure")
] + ["malformed-archive", "candidate-conflict"]


def run_case(command, name):
    description = get_description(command)
    if name == "independent-cases" or "fanout" in name:
        cases = deepcopy(description["cases"])
        cases.append(dict(cases[0], id="case-b"))
        description = get_description(command, {"cases": cases, "legacy": name.startswith("legacy-")})
    with tempfile.TemporaryDirectory(prefix="w08-extended-") as directory:
        session = Session(command, directory, description["providerResponse"],
                          check=name not in ("malformed-archive", "candidate-conflict"))
        failure, details = None, {}
        try:
            if name == "independent-cases":
                case_independence(session, description, details)
            elif "fanout" in name:
                fanout(session, description, details, name.rsplit("-", 1)[1])
            elif name.startswith("save-"):
                save_order(session, description, details, name.removeprefix("save-"), command)
            elif name == "malformed-archive":
                malformed_archive(session, description, details)
            elif name == "candidate-conflict":
                candidate_conflict(session, description, details)
            else:
                operation, schedule = name.split("-", 1)
                if schedule == "advance-after-commit":
                    view_advance(session, description, details, operation)
                elif schedule == "stale-etag":
                    view_stale_etag(session, description, details, operation)
                elif schedule == "bounded-rebuild":
                    view_bound(session, description, details, operation)
                elif schedule == "partial-list-failure":
                    view_listing_failure(session, description, details, operation, command)
                else:
                    raise ValueError("Unknown extended schedule")
        except Exception as error:
            failure = {"code": getattr(error, "code", type(error).__name__), "detail": str(error)}
        finally:
            try:
                snapshot = session.authority.snapshot()
                result = {"scenario": name, "failure": failure, "details": details,
                          "events": session.events, "actions": session.steps,
                          "oracle_checks": session.observer.checks,
                          "provider_effects": len(session.authority.effects()), "objects": len(snapshot),
                          "immutable_sha256": {key: digest(value.body) for key, value in snapshot.items()
                                               if key.startswith("promotion/candidates/") or
                                               (key.startswith("runs/") and key[5:9].isdigit())},
                          "operation_counts": {operation: sum(child.spec["operation"] == operation for child in session.children)
                                               for operation in sorted({child.spec["operation"] for child in session.children})}}
                result["model"] = session.model.report()
                result["observation_scope"] = ("snapshot and independent trace observers, plus schedule assertions"
                                               if session.check else
                                               "explicit corruption rejection assertions; seeded-invalid records excluded from observers")
            finally:
                session.close()
    return result


def event_digest(result):
    # Captures the forced request/commit/reply schedule, independent of production
    # clock fields and per-request random SDK correlation IDs (not in events).
    # Candidate IDs hash the real production CheckedAtUtc. Preserve their path,
    # count and repeated identity while normalizing that per-run generated hash.
    candidates, events, returns = {}, [], []
    for original in result["events"]:
        event = {key: value for key, value in original.items() if key != "step"}
        # await_boundaries waits for every child before scheduling. OS arrival
        # order of those already-held requests is observational, not a choice.
        if event["event"] == "request":
            continue
        if event["event"] == "return":
            returns.append(event)
            continue
        name = event.get("object", "")
        if re.fullmatch(r"promotion/candidates/[^/]+/[a-f0-9]{64}\.json", name):
            candidates.setdefault(name, len(candidates))
            event["object"] = name.rsplit("/", 1)[0] + f"/candidate-{candidates[name]}.json"
        events.append(event)
    normalized = {"actions": events, "returns": sorted(returns, key=lambda item: item["worker"])}
    return digest(json.dumps(normalized, sort_keys=True, separators=(",", ":")).encode())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--worker", type=Path, default=ROOT / "NotaryGeek.PublicKnowledge.Worker.Tests/bin/W08Worker/Debug/net10.0/W08Worker.dll")
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--scenario", choices=["all", *NAMES], default="all")
    parser.add_argument("--replay", type=Path, help="Replay one exact source-bound scripted schedule")
    args = parser.parse_args()
    command = [args.dotnet, "exec", str(args.worker)]
    binding = source_binding(args.worker)
    source_head = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    replay = json.loads(args.replay.read_text()) if args.replay else None
    if replay:
        if replay.get("source_binding") != binding:
            raise ValueError("Replay source/executable binding differs from the recorded extended schedule.")
        names = [replay["result"]["scenario"]]
        if names[0] not in NAMES:
            raise ValueError("Unknown recorded extended scenario.")
    else:
        names = NAMES if args.scenario == "all" else [args.scenario]
    args.out.mkdir(parents=True, exist_ok=True)
    summaries = []
    for name in names:
        result = run_case(command, name)
        result["event_digest"] = event_digest(result)
        if replay and (result["event_digest"] != replay["result"]["event_digest"] or
                       (result["failure"] or {}).get("code") != (replay["result"].get("failure") or {}).get("code")):
            result["failure"] = {"code": "extended-replay-diverged", "detail": "Scripted HTTP boundaries or outcome differed."}
        record = {"source_head": source_head, "source_binding": binding,
                  "bounds": {"actions": 3500, "deadline_seconds": 90, "derived_rebuild_attempts": 10},
                  "scope": "fixed causal schedules; direct save/admin operations are explicit; no exhaustive claim",
                  "result": result}
        (args.out / (name + ".json")).write_text(json.dumps(record, indent=2) + "\n")
        summary = {key: result[key] for key in ("scenario", "failure", "details", "actions", "provider_effects", "oracle_checks", "operation_counts")}
        summaries.append(summary)
        print(json.dumps(summary), flush=True)
    changed = source_binding(args.worker) != binding
    report = {"source_head": source_head, "source_binding": binding, "source_changed_during_run": changed,
              "schedules": len(summaries), "failed": sum(bool(item["failure"]) for item in summaries) + int(changed),
              "results": summaries}
    (args.out / "summary.json").write_text(json.dumps(report, indent=2) + "\n")
    return bool(report["failed"])


if __name__ == "__main__":
    raise SystemExit(main())
