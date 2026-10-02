#!/usr/bin/env python3
"""Production identity/replay schedules and failure-preserving operation reduction."""
import argparse
from copy import deepcopy
import json
from pathlib import Path
import tempfile

from w08_explore import Session, ROOT, boundary, get_description, require, source_binding


def job_record(s, message):
    return next(json.loads(b.body) for n, b in s.authority.snapshot().items()
                if n.startswith("runs/jobs/") and json.loads(b.body)["jobId"] == message["jobId"])


def attempt(s, spec, name):
    child = s.start(spec, name)
    s.run()
    return child.result


def run_case(command, name, operations=None):
    base = get_description(command)
    cases = deepcopy(base["cases"])
    if name in ("legacy-catalog-casing", "fingerprint-order"):
        cases.append(dict(cases[0], id="case-b"))
        base = get_description(command, {"cases": cases, "legacy": name == "legacy-catalog-casing"})
    m = deepcopy(base["message"])
    child_message = dict(m, caseId="case-a")
    allowed = operations or ["create", "prepare", "subject", "replay"]
    with tempfile.TemporaryDirectory(prefix="w08-regression-") as directory:
        # Continue past an abstract identity disagreement to the concrete durable
        # failure (e.g. actual second provider effect), using the snapshot oracle.
        s = Session(command, directory, base["providerResponse"], policy="round-robin", model_check=False)
        spec = lambda op, msg=child_message, catalog=cases, **kwargs: {
            "operation": op, "message": msg, "cases": catalog, **kwargs}
        failure = None
        try:
            if "create" in allowed:
                s.single(spec("create", m), "create")
            if "prepare" in allowed:
                if name == "legacy-catalog-casing":
                    s.single(spec("run", m), "fanout")
                elif name == "fingerprint-order":
                    s.fault = {"worker": "prepare", "boundary": "archive", "stage": "response", "action": "kill"}
                    attempt(s, spec("run"), "prepare")
                    require(s.fired == 1, "fixture-prerequisite", "archive kill not reached")
                    s.fault = None
                else:
                    if name == "missing-failed-catalog":
                        s.provider_response = {"status": "failed", "error": {"message": "synthetic fixture failure"}}
                    s.single(spec("run"), "prepare")
            before = job_record(s, m) if "create" in allowed else None
            if "subject" in allowed:
                if name == "legacy-catalog-casing":
                    lower = s.start(spec("run"), "lowercase")
                    while True:
                        s.await_boundaries()
                        if lower.stage == "response" and boundary(lower.request) == "provider":
                            break
                        require(lower.stage != "done", "fixture-prerequisite", "lowercase provider not reached")
                        s.advance(lower)
                    alternate = [dict(cases[0], id="CASE-A"), cases[1]]
                    upper = s.start(spec("run", catalog=alternate), "uppercase")
                    while upper.stage != "done":
                        s.await_boundaries()
                        if upper.stage != "done":
                            s.advance(upper)
                    s.run()
                elif name == "unknown-child":
                    attempt(s, spec("run", dict(m, caseId="unknown-case")), "subject")
                    after = job_record(s, m)
                    require(after["receipts"] == before["receipts"] and after["status"] == before["status"],
                            "unknown-child-mutated-job", "unsubmitted child altered prior receipts/status")
                elif name == "missing-failed-catalog":
                    alternate = [dict(cases[0], id="case-absent")]
                    attempt(s, spec("run", catalog=alternate), "subject")
                    after = job_record(s, m)
                    require(after["receipts"] == before["receipts"], "failed-evidence-receipt-replaced",
                            "catalog-missing duplicate replaced recorded provider evidence")
                elif name == "fingerprint-order":
                    reordered = dict(child_message, caseFingerprints=dict(reversed(list(m["caseFingerprints"].items()))))
                    result = attempt(s, spec("run", reordered), "subject")
                    require(result["ok"], "equivalent-map-recovery-blocked", result.get("errorType", "failed"))
            if "replay" in allowed:
                if name != "fingerprint-order":
                    s.single(spec("run"), "replay")
                require(len(s.authority.effects()) <= 1, "provider-repeated", "same submitted case invoked twice")
        except Exception as ex:
            failure = {"code": getattr(ex, "code", type(ex).__name__), "detail": str(ex)}
        finally:
            try:
                result = {"case": name, "operations": allowed, "failure": failure,
                          "events": s.events, "provider_effects": len(s.authority.effects()),
                          "oracle_checks": s.observer.checks, "actions": s.steps}
            finally:
                s.close()
    return result


def minimize(command, name, original):
    """Delete operations, rerun actual processes, preserve exact failure code.

    This reduces causal operations rather than deleting events from a recorded
    trace. Every accepted reduction is a fresh production execution.
    """
    current = original
    attempts = 0
    for op in original["operations"]:
        candidate = [x for x in current["operations"] if x != op]
        if not candidate:
            continue
        reduced = run_case(command, name, candidate)
        attempts += 1
        if (reduced["failure"] or {}).get("code") == original["failure"]["code"]:
            current = reduced
    return current, attempts


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--dotnet", default="dotnet")
    p.add_argument("--worker", type=Path, default=ROOT / "NotaryGeek.PublicKnowledge.Worker.Tests/bin/W08Worker/Debug/net10.0/W08Worker.dll")
    p.add_argument("--out", type=Path, required=True)
    p.add_argument("--case", choices=["all", "unknown-child", "legacy-catalog-casing", "missing-failed-catalog", "fingerprint-order"], default="all")
    p.add_argument("--expect-original-failures", action="store_true")
    p.add_argument("--minimize", action="store_true")
    p.add_argument("--operations", help="Comma-separated reduced operation sequence")
    args = p.parse_args()
    command = [args.dotnet, "exec", str(args.worker)]
    binding = source_binding(args.worker)
    names = ["unknown-child", "legacy-catalog-casing", "missing-failed-catalog", "fingerprint-order"] if args.case == "all" else [args.case]
    args.out.mkdir(parents=True, exist_ok=True)
    results = []
    for name in names:
        result = run_case(command, name, args.operations.split(",") if args.operations else None)
        reduction = None
        if args.minimize and result["failure"]:
            reduction, attempts = minimize(command, name, result)
            reduction["reduction_attempts"] = attempts
        record = {"source_binding": binding, "result": result, "minimized": reduction}
        (args.out / (name + ".json")).write_text(json.dumps(record, indent=2) + "\n")
        summary = {k: result[k] for k in ("case", "failure", "provider_effects", "actions")}
        summary["minimized_operations"] = reduction["operations"] if reduction else None
        results.append(summary)
        print(json.dumps(summary), flush=True)
    changed = binding != source_binding(args.worker)
    failures = sum(bool(r["failure"]) for r in results)
    report = {"results": results, "source_changed_during_run": changed, "source_binding": binding}
    (args.out / "summary.json").write_text(json.dumps(report, indent=2) + "\n")
    return changed or (failures != len(names) if args.expect_original_failures else failures != 0)


if __name__ == "__main__":
    raise SystemExit(main())
