#!/usr/bin/env python3
"""One deterministic, standard-library command for W08 abstract evidence.

    python NotaryGeek.PublicKnowledge.Worker.Tests/W08ModelChecking/w08_model_tests.py

Prints a machine-readable report to stdout and exits nonzero on any unexpected
result. Deliberate illegal histories and a broken conditional-reservation
primitive must fail; their minimized failures are expected evidence, not test
failures. No storage, provider, network, clock, random seed, or production binary
is needed. Production trace checking imports w08_model.validate_history instead.
"""

from __future__ import annotations

from dataclasses import asdict
import json
import unittest

from w08_model import (Bounds, Case, explore, fair_finish, initial,
                       minimize_history, successors, validate_history)


def seed(job="j0", case="a", eid="e0", submitted=1, worker="w0"):
    common = {"job": job, "case": case}
    return [
        {"op": "admit", **common, "worker": worker, "fingerprint": "fp", "submitted_at": submitted},
        {"op": "provider", **common, "worker": worker},
        {"op": "archive", **common, "fingerprint": "fp", "submitted_at": submitted,
         "evidence_id": eid, "payload": "bytes:" + eid},
        {"op": "phase", **common, "phase": "evidence"},
        {"op": "receipt", **common, "status": "succeeded", "evidence_id": eid},
        {"op": "candidate", **common, "evidence_id": eid},
        {"op": "phase", **common, "phase": "candidates"},
        {"op": "latest", **common, "evidence_id": eid, "submitted_at": submitted},
    ]


def saved_view(kind="index", cases=("a",), ids=("e0",), name="one"):
    return [
        {"op": "list_latest", "listing": "l:" + name, "cases": list(cases)},
        *[{"op": "read_latest", "read": "r:" + name + ":" + case, "case": case, "evidence_id": eid}
          for case, eid in zip(cases, ids)],
        {"op": "observe", "observation": "o:" + name, "listing": "l:" + name,
         "reads": ["r:" + name + ":" + case for case in cases]},
        {"op": "view", "kind": kind, "observation": "o:" + name, "evidence_set": list(ids)},
    ]


def completed_history():
    return seed() + saved_view() + [
        {"op": "phase", "job": "j0", "case": "a", "phase": "index"},
    ] + saved_view("digest", name="two") + [
        {"op": "phase", "job": "j0", "case": "a", "phase": "digest"},
        {"op": "ack", "job": "j0", "case": "a"},
    ]


def illegal_histories():
    base = seed()
    return {
        "retry-after-provider-uncertainty": (base[:2] + [
            {"op": "crash", "worker": "w0"}, {"op": "recover", "worker": "w1"},
            {"op": "provider", "job": "j0", "case": "a", "worker": "w1"}], "provider_once"),
        "overwrite-immutable-evidence": (base + [{**base[2], "payload": "different-bytes"}], "archive_immutable"),
        "fingerprint-drift": (base[:2] + [{**base[2], "fingerprint": "changed"}], "archive_bound"),
        "cross-case-receipt": (base + [{"op": "receipt", "job": "j0", "case": "b",
                                       "status": "succeeded", "evidence_id": "e0"}], "receipt_evidence"),
        "cross-job-evidence": (base + seed("j1", "a", "e0", 2), "archive_immutable"),
        "success-downgrade": (base + [{"op": "receipt", "job": "j0", "case": "a", "status": "failed"}], "receipt_monotonic"),
        "success-retarget": (base + [{"op": "receipt", "job": "j0", "case": "a", "status": "succeeded",
                                     "evidence_id": "other"}], "receipt_monotonic"),
        "phase-skipped": (base[:3] + [{"op": "phase", "job": "j0", "case": "a", "phase": "digest"}], "phase_order"),
        "phase-regressed": (base + [{"op": "phase", "job": "j0", "case": "a", "phase": "evidence"}], "phase_order"),
        "marker-without-evidence": (base[:2] + [{"op": "phase", "job": "j0", "case": "a", "phase": "evidence"}], "phase_backing"),
        "candidate-marker-without-candidate": (base[:5] + [base[6]], "phase_backing"),
        "index-marker-without-view": (base + [{"op": "phase", "job": "j0", "case": "a", "phase": "index"}], "phase_backing"),
        "latest-submitted-time-regression": (base + seed("j1", "a", "e1", 2) + [base[7]], "latest_order"),
        "latest-tie-identity-regression": (base + seed("j1", "a", "e1", 1) + [base[7]], "latest_order"),
        "latest-time-spoof": (base + [{**base[7], "submitted_at": 9}], "latest_identity"),
        "completed-before-publication": (base + [{"op": "ack", "job": "j0", "case": "a"}], "premature_ack"),
        "view-omits-listed-case": (base + seed("j1", "b", "e1", 2) + [
            {"op": "list_latest", "listing": "l", "cases": ["a", "b"]},
            {"op": "read_latest", "read": "r", "case": "a", "evidence_id": "e0"},
            {"op": "observe", "observation": "o", "listing": "l", "reads": ["r"]}], "view_complete"),
        "view-drops-observed-evidence": (base + saved_view() + [
            {"op": "view", "kind": "index", "observation": "o:one", "evidence_set": []}], "view_complete"),
        "unknown-durable-phase": (base + [{"op": "phase", "job": "j0", "case": "a", "phase": "admitted"}], "schema"),
        "unacknowledged-admission-used": ([{**base[0], "acknowledged": False}, base[1]], "provider_authorized"),
        "provider-after-crash-without-fresh-authority": ([base[0], {"op": "crash", "worker": "w0"}, base[1]], "provider_authorized"),
    }


class OracleTests(unittest.TestCase):
    def test_complete_and_idempotent_evidence_replay(self):
        history = completed_history()
        self.assertEqual([], validate_history(history + [seed()[2], seed()[4]]))

    def test_commit_before_acknowledgement_loss_is_legal(self):
        history = seed()
        history[2] = {**history[2], "acknowledged": False}
        history[3:3] = [{"op": "crash", "worker": "w0"}, {"op": "recover", "worker": "w1"}]
        self.assertEqual([], validate_history(history))
        self.assertEqual([], validate_history([{**seed()[0], "acknowledged": False}]))

    def test_uncommitted_writes_have_no_durable_effect(self):
        history = seed()
        self.assertEqual([], validate_history(history + [{**history[2], "payload": "not-written", "committed": False}]))

    def test_distinct_job_case_identities_are_isolated(self):
        self.assertEqual([], validate_history(seed() + seed("j1", "b", "e1", 2)))
        self.assertEqual([], validate_history(seed() + seed("j0", "b", "e1", 2)))

    def test_unknown_outcome_does_not_promise_liveness(self):
        cases, bounds = [Case("j0", "a", 1, "e0")], Bounds()
        state, history = initial(cases, bounds), []
        for label in ("deliver", "reserve", "provider-outcome-unknown"):
            _, state, events = next(item for item in successors(state, cases, bounds) if item[0] == label)
            history.extend(events)
        final, _, report = fair_finish(state, history, cases)
        self.assertEqual(0, report["eligible_recorded_cases"])
        self.assertEqual(1, report["excluded_unknown_cases"])
        self.assertEqual(1, final.durable[0].calls)
        self.assertFalse(final.durable[0].ack)
        self.assertEqual([], report["safety_violations"])

    def test_archive_commit_response_loss_has_fair_recovery_suffix(self):
        cases, bounds = [Case("j0", "a", 1, "e0")], Bounds()
        state, history = initial(cases, bounds), []
        for label in ("deliver", "reserve", "provider-return", "archive-response-lost"):
            _, state, events = next(item for item in successors(state, cases, bounds) if item[0] == label)
            history.extend(events)
        final, _, report = fair_finish(state, history, cases)
        self.assertTrue(final.durable[0].ack)
        self.assertTrue(report["provider_counts_unchanged"])
        self.assertEqual([], report["safety_violations"])

    def test_mixed_time_observation_and_temporarily_stale_view_are_legal(self):
        history = seed() + seed("j1", "b", "e1", 1) + [
            {"op": "list_latest", "listing": "mixed", "cases": ["a", "b"]},
            {"op": "read_latest", "read": "old-a", "case": "a", "evidence_id": "e0"},
        ] + seed("j2", "a", "e2", 2) + seed("j3", "b", "e3", 2) + [
            {"op": "read_latest", "read": "new-b", "case": "b", "evidence_id": "e3"},
            {"op": "observe", "observation": "mixed", "listing": "mixed", "reads": ["old-a", "new-b"]},
            {"op": "view", "kind": "index", "observation": "mixed", "evidence_set": ["e0", "e3"]},
        ]
        # e0/e3 never coexisted as the global pointer set. It is nevertheless a
        # legal aggregate of separate reads; a pending rebuild must repair it.
        self.assertEqual([], validate_history(history))

    def test_pointer_added_after_listing_does_not_invent_atomicity(self):
        history = seed() + [{"op": "list_latest", "listing": "old-list", "cases": ["a"]}]
        history += seed("j1", "b", "e1", 2) + [
            {"op": "read_latest", "read": "a-read", "case": "a", "evidence_id": "e0"},
            {"op": "observe", "observation": "old", "listing": "old-list", "reads": ["a-read"]},
            {"op": "view", "kind": "index", "observation": "old", "evidence_set": ["e0"]},
        ]
        self.assertEqual([], validate_history(history))

    def test_older_completion_may_be_covered_by_newer_evidence(self):
        history = seed() + seed("j1", "a", "e1", 2) + saved_view(ids=("e1",)) + [
            {"op": "phase", "job": "j0", "case": "a", "phase": "index"},
        ]
        self.assertEqual([], validate_history(history))

    def test_view_checks_can_be_explicitly_disabled_for_partial_trace(self):
        history = seed() + [{"op": "phase", "job": "j0", "case": "a", "phase": "index"}]
        self.assertEqual([], validate_history(history, check_views=False))
        self.assertEqual("phase_backing", validate_history(history)[0].code)

    def test_illegal_histories_fail_for_the_expected_invariant(self):
        for name, (history, code) in illegal_histories().items():
            with self.subTest(name=name):
                errors = validate_history(history)
                self.assertTrue(errors)
                self.assertEqual(code, errors[0].code)

    def test_minimization_is_deterministic_and_preserves_failure_kind(self):
        history, code = illegal_histories()["retry-after-provider-uncertainty"]
        predicate = lambda events: bool(validate_history(events)) and validate_history(events)[0].code == code
        result = minimize_history(history, predicate)
        self.assertEqual(result, minimize_history(history, predicate))
        self.assertTrue(predicate(result))
        for index in range(len(result)):
            self.assertFalse(predicate(result[:index] + result[index + 1:]))

    def test_bounds_are_reported_as_truncation(self):
        cases = [Case("j0", "a", 1, "e0")]
        self.assertTrue(explore(cases, Bounds(states=2)).truncated)
        shallow = explore(cases, Bounds(depth=1))
        self.assertTrue(shallow.truncated)
        self.assertGreater(shallow.depth_limited, 0)


def run_checks():
    """Reusable deterministic report; assertion failure must fail the caller."""
    bounds = Bounds(workers=2, deliveries=3, crashes=1, depth=60, states=50000)
    scenarios = {
        "duplicate-delivery-one-case": [Case("j0", "a", 1, "e0")],
        "two-cases-same-job": [Case("j0", "a", 1, "e0"), Case("j0", "b", 1, "e1")],
        "two-jobs-latest-submitted-time": [Case("j0", "a", 1, "e0"), Case("j1", "a", 2, "e1")],
        "two-jobs-latest-evidence-tie-break": [Case("j0", "a", 1, "e0"), Case("j1", "a", 1, "e1")],
    }
    reports = {}
    for name, cases in scenarios.items():
        result = explore(cases, bounds, check_liveness=True)
        assert result.violation is None, (name, result.violation, result.counterexample)
        assert not result.truncated, (name, "configured bounded exploration did not exhaust")
        assert result.liveness_projections_checked > 0
        reports[name] = asdict(result)
    broken = explore(scenarios["duplicate-delivery-one-case"], bounds, broken_conditional=True)
    assert broken.violation and broken.violation.code == "admission_unique", asdict(broken)
    code = broken.violation.code
    preserves = lambda events: bool(validate_history(events)) and validate_history(events)[0].code == code
    minimized = minimize_history(broken.counterexample, preserves)
    assert len(minimized) < len(broken.counterexample) or len(minimized) == 2
    assert preserves(minimized)
    negatives = {}
    for name, (history, expected) in illegal_histories().items():
        errors = validate_history(history)
        assert errors and errors[0].code == expected, name
        predicate = lambda events, expected=expected: bool(validate_history(events)) and validate_history(events)[0].code == expected
        minimal = minimize_history(history, predicate)
        assert predicate(minimal)
        negatives[name] = {"violation": expected, "original_events": len(history), "minimized_events": len(minimal)}
    return {
        "schema_version": 1,
        "kind": "bounded-independent-abstract-model-evidence",
        "fairness": "faults cease; storage operations terminate; pointers quiesce; every recorded-evidence case receives a fair replay suffix",
        "liveness_scope": "finite fault-free recovery witnesses for every enumerated durable recovery projection; unknown reservations without evidence excluded",
        "equivalence": "worker permutation plus previously visited complete abstract states; no job/case/evidence/order identities removed",
        "schedule_count_scope": "transitions count explored schedule-prefix edges, not all unpruned execution permutations",
        "claim_limit": "finite abstraction and configured bounds only; no universal proof, external exactly-once claim, or global pointer/view transaction",
        "scenarios": reports,
        "negative_histories": negatives,
        "broken_conditional_primitive": {
            "description": "faulty conditional reservation grants again when identity already reserved",
            "detected": asdict(broken.violation),
            "states": broken.states, "transitions": broken.transitions,
            "schedule": broken.schedule,
            "original_history": broken.counterexample,
            "minimized_history": minimized,
            "minimization": "deterministic failure-code-preserving delta debugging; 1-minimal, not globally shortest",
        },
    }


if __name__ == "__main__":
    suite = unittest.defaultTestLoader.loadTestsFromTestCase(OracleTests)
    result = unittest.TextTestRunner(verbosity=1).run(suite)
    if not result.wasSuccessful():
        raise SystemExit(1)
    report = run_checks()
    report["oracle_tests"] = result.testsRun
    print(json.dumps(report, indent=2, sort_keys=True))
