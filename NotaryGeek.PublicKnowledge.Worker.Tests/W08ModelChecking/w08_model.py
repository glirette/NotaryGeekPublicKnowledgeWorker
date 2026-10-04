#!/usr/bin/env python3
"""Independent, finite abstraction of the public queued-worker contract.

No Azure SDK, production code, filesystem interpretation, or recovery policy is
used here. The model is a transition system; the history oracle is a separately
written specification over externally observable events. Bounds are evidence,
not a proof for arbitrary executions. Provider calls mean attempted boundaries,
not a claim about external exactly-once effects.

Normalized history schema (all events are dictionaries with ``op``):
  admit: job, case, fingerprint, submitted_at, worker, granted (default true)
  provider: job, case, worker
  archive: job, case, fingerprint, submitted_at, evidence_id, payload
  candidate: job, case, evidence_id
  phase: job, case, phase (evidence, candidates, index, digest)
  latest: job, case, evidence_id, submitted_at
  receipt: job, case, status (succeeded, failed, unknown), evidence_id on success
  ack: job, case -- a completed case acknowledgement, NOT any method return
  crash / recover: worker
  list_latest: listing, cases (source case IDs)
  read_latest: read, case, evidence_id
  observe: observation, listing, reads (read IDs, one for every listed case)
  view: kind (index/digest), observation, evidence_set (evidence IDs)

Mutation events may carry committed=false (no durable mutation) and
acknowledged=false (commit occurred but response was lost). Admission with a
lost acknowledgement reserves durably but grants no local provider authority.
``submitted_at`` must be an integer or an ISO 8601 timestamp with timezone;
``payload`` is an exact, opaque byte-identity string (a harness may supply SHA256).
Use a complete synthetic history, including any initial persisted seed events.

Views are optional. With check_views=True (the default), index/digest markers
need corresponding view evidence. With False, the oracle checks marker ordering
only, and reports no claim about view completeness. A listing plus individual
reads can form a mixed-time observation: no transaction across pointers is
assumed, and a saved view may be temporarily stale after pointer advancement.
"""

from __future__ import annotations

from collections import deque
from dataclasses import dataclass, replace
from datetime import datetime, timezone
import json
import re
from typing import Callable, Iterable, Iterator, Sequence


PHASES = ("none", "evidence", "candidates", "index", "digest")


@dataclass(frozen=True)
class Violation:
    code: str
    index: int
    detail: str


class _Invalid(Exception):
    def __init__(self, code: str, detail: str):
        self.code, self.detail = code, detail


def _require(condition: bool, code: str, detail: str) -> None:
    if not condition:
        raise _Invalid(code, detail)


def _time(value: object) -> int:
    if isinstance(value, int) and not isinstance(value, bool):
        return value
    match = re.fullmatch(r"(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(Z|[+-]\d{2}:\d{2})", str(value))
    _require(match is not None, "schema", "timestamp needs ISO UTC/offset and at most seven fractional digits")
    whole, fraction, offset = match.groups()
    parsed = datetime.fromisoformat(whole + offset.replace("Z", "+00:00")).astimezone(timezone.utc)
    epoch = datetime(1970, 1, 1, tzinfo=timezone.utc)
    delta = parsed - epoch
    # .NET DateTime is precise to 100 ns; datetime.fromisoformat would silently
    # truncate the seventh digit and could misclassify latest-pointer ordering.
    return (delta.days * 86400 + delta.seconds) * 10_000_000 + int((fraction or "").ljust(7, "0"))


def validate_history(events: Iterable[dict], *, check_views: bool = True) -> list[Violation]:
    """Return the first invariant violation, or []. Never consult production state.

    First-failure semantics make failure-preserving minimization unambiguous.
    Unknown operations and malformed events fail closed rather than disappearing.
    """
    reservations: dict[tuple[str, str], tuple] = {}
    authority: set[tuple[tuple[str, str], str]] = set()
    calls: dict[tuple[str, str], int] = {}
    evidence: dict[str, tuple] = {}
    by_case: dict[tuple[str, str], str] = {}
    candidates: set[str] = set()
    phases: dict[tuple[str, str], int] = {}
    receipts: dict[tuple[str, str], tuple] = {}
    latest: dict[str, str] = {}
    listings: dict[str, tuple] = {}
    reads: dict[str, tuple] = {}
    observations: dict[str, tuple] = {}
    views: dict[str, list[tuple]] = {}

    def order(eid: str) -> tuple:
        item = evidence[eid]
        return item[2], eid

    def covers(kind: str, key: tuple[str, str]) -> bool:
        if not check_views:
            return True
        own = by_case.get(key)
        return own is not None and any(
            evidence[eid][0][1] == key[1] and order(eid) >= order(own)
            for saved in views.get(kind, ()) for eid in saved
        )

    def bind_id(table: dict, name: str, value: tuple) -> None:
        _require(name not in table or table[name] == value,
                 "identity", "observation identity reused with different contents")
        table[name] = value

    for index, event in enumerate(events):
        try:
            op = event["op"]
            _require(isinstance(op, str), "schema", "operation must be a string")
            if op in {"admit", "archive", "candidate", "phase", "latest", "receipt", "view"}:
                if event.get("committed", True) is False:
                    continue
            if op in {"crash", "recover"}:
                worker = event["worker"]
                if op == "crash":
                    authority = {item for item in authority if item[1] != worker}
                continue
            if op == "list_latest":
                cases = tuple(sorted(event["cases"]))
                _require(len(set(cases)) == len(cases) and set(cases) == set(latest),
                         "view_complete", "listing omitted or invented a current pointer")
                bind_id(listings, event["listing"], cases)
                continue
            if op == "read_latest":
                case, eid = event["case"], event["evidence_id"]
                _require(latest.get(case) == eid, "view_evidence", "read was not the pointer at its linearization")
                bind_id(reads, event["read"], (case, eid))
                continue
            if op == "observe":
                listed = listings[event["listing"]]
                items = [reads[name] for name in event["reads"]]
                _require(len(items) == len(listed) and sorted(x[0] for x in items) == list(listed),
                         "view_complete", "every listed case needs exactly one readable pointer")
                bind_id(observations, event["observation"], tuple(sorted(x[1] for x in items)))
                continue
            if op == "view":
                kind = event["kind"]
                _require(kind in {"index", "digest"}, "schema", "unknown view kind")
                saved = tuple(sorted(event["evidence_set"]))
                _require(saved == observations[event["observation"]],
                         "view_complete", "derived write did not preserve its observed evidence set")
                # A phase records a completed publication step, not a lock on
                # a global derived view. Concurrent rebuilds may subsequently
                # leave a temporarily stale snapshot while they retry.
                views.setdefault(kind, []).append(saved)
                continue
            _require(op in {"admit", "provider", "archive", "candidate", "phase", "latest", "receipt", "ack"},
                     "schema", "unknown operation: " + op)
            key = (event["job"], event["case"])
            _require(all(isinstance(part, str) and part for part in key), "identity", "empty job/case identity")
            if op == "admit":
                binding = (event["fingerprint"], _time(event["submitted_at"]))
                _require(isinstance(binding[0], str) and bool(binding[0]), "identity", "missing fingerprint")
                if not event.get("granted", True):
                    continue
                _require(key not in reservations, "admission_unique", "conditional reservation granted twice")
                reservations[key] = binding
                if event.get("acknowledged", True):
                    authority.add((key, event["worker"]))
            elif op == "provider":
                _require(calls.get(key, 0) == 0, "provider_once", "second provider boundary for admitted identity")
                _require((key, event["worker"]) in authority, "provider_authorized", "no acknowledged fresh admission")
                calls[key] = 1
                authority.remove((key, event["worker"]))
            elif op == "archive":
                eid = event["evidence_id"]
                bound = (event["fingerprint"], _time(event["submitted_at"]))
                _require(reservations.get(key) == bound, "archive_bound", "evidence differs from admitted identity/fingerprint/time")
                _require(calls.get(key) == 1, "archive_bound", "evidence has no attempted provider boundary")
                item = (key, bound[0], bound[1], event["payload"])
                _require(eid not in evidence or evidence[eid] == item,
                         "archive_immutable", "immutable evidence identity was overwritten")
                _require(key not in by_case or by_case[key] == eid,
                         "archive_immutable", "case gained a second evidence identity")
                evidence[eid], by_case[key] = item, eid
            elif op == "candidate":
                eid = event["evidence_id"]
                _require(by_case.get(key) == eid and phases.get(key, 0) >= 1,
                         "phase_backing", "candidate lacks recorded evidence")
                candidates.add(eid)
            elif op == "phase":
                name = event["phase"]
                _require(name in PHASES[1:], "schema", "unknown durable phase")
                value, prior = PHASES.index(name), phases.get(key, 0)
                _require(value >= prior and value <= prior + 1,
                         "phase_order", "publication phase skipped a predecessor or regressed")
                _require(key in by_case, "phase_backing", "phase marker lacks immutable evidence")
                if value >= 2:
                    _require(by_case[key] in candidates, "phase_backing", "candidate marker lacks candidate")
                if value == 3:
                    _require(covers("index", key), "phase_backing", "index marker lacks covering index view")
                if value == 4:
                    _require(covers("digest", key), "phase_backing", "digest marker lacks covering digest view")
                phases[key] = value
            elif op == "latest":
                eid = event["evidence_id"]
                _require(by_case.get(key) == eid and evidence[eid][2] == _time(event["submitted_at"]),
                         "latest_identity", "pointer identity/time differs from immutable evidence")
                previous = latest.get(key[1])
                _require(previous is None or order(eid) >= order(previous),
                         "latest_order", "latest pointer regressed submitted-time/evidence-identity order")
                latest[key[1]] = eid
            elif op == "receipt":
                status = event["status"]
                _require(status in {"succeeded", "failed", "unknown"}, "schema", "unknown receipt status")
                value = (status, event.get("evidence_id"))
                prior = receipts.get(key)
                _require(prior is None or prior[0] != "succeeded" or prior == value,
                         "receipt_monotonic", "successful receipt changed or downgraded")
                if status == "succeeded":
                    _require(value[1] == by_case.get(key) and phases.get(key, 0) >= 1,
                             "receipt_evidence", "successful receipt lacks matching recorded evidence")
                receipts[key] = value
            elif op == "ack":
                _require(receipts.get(key, (None,))[0] == "succeeded" and phases.get(key) == 4,
                         "premature_ack", "completed acknowledgement lacks success/evidence/publication")
        except _Invalid as exc:
            return [Violation(exc.code, index, exc.detail)]
        except (KeyError, TypeError, ValueError, AttributeError) as exc:
            return [Violation("schema", index, "malformed event: " + str(exc))]
    return []


def minimize_history(history: Sequence[dict], fails: Callable[[list[dict]], bool]) -> list[dict]:
    """Deterministic delta debugging; preserve the caller's exact failure predicate.

    The final single-deletion pass makes the result 1-minimal, not globally
    shortest. Pass a violation-code predicate to avoid shrinking to a schema
    error. The same utility can minimize concrete production schedules/traces.
    """
    current = list(history)
    if not fails(current):
        raise ValueError("the input history does not exhibit the requested failure")
    partitions = 2
    while len(current) >= 2:
        width = (len(current) + partitions - 1) // partitions
        reduced = False
        for start in range(0, len(current), width):
            attempt = current[:start] + current[start + width:]
            if fails(attempt):
                current, partitions, reduced = attempt, max(2, partitions - 1), True
                break
        if not reduced:
            if partitions >= len(current):
                break
            partitions = min(len(current), partitions * 2)
    index = 0
    while index < len(current):
        attempt = current[:index] + current[index + 1:]
        if fails(attempt):
            current = attempt
            index = 0
        else:
            index += 1
    return current


@dataclass(frozen=True)
class Case:
    job: str
    case: str
    submitted_at: int
    evidence_id: str
    fingerprint: str = "fp"

    def event(self, op: str, **fields: object) -> dict:
        return {"op": op, "job": self.job, "case": self.case, **fields}


@dataclass(frozen=True)
class Durable:
    reserved: bool = False
    calls: int = 0
    archived: bool = False
    phase: int = 0
    candidate: bool = False
    receipt: bool = False
    ack: bool = False


@dataclass(frozen=True)
class Worker:
    target: int = -1
    pc: str = "idle"
    listing: tuple[str, ...] = ()
    snapshot: tuple[int, ...] = ()
    expected_view: tuple[int, ...] = ()


@dataclass(frozen=True)
class State:
    durable: tuple[Durable, ...]
    workers: tuple[Worker, ...]
    latest: tuple[int, ...] = ()
    index: tuple[int, ...] = ()
    digest: tuple[int, ...] = ()
    deliveries: int = 0
    crashes: int = 0


@dataclass(frozen=True)
class Bounds:
    workers: int = 2
    deliveries: int = 3
    crashes: int = 1
    depth: int = 32
    states: int = 20000


def initial(cases: Sequence[Case], bounds: Bounds) -> State:
    return State(tuple(Durable() for _ in cases), tuple(Worker() for _ in range(bounds.workers)))


def _listing_name(names: tuple[str, ...]) -> str:
    return "list:" + json.dumps(names, separators=(",", ":"))


def _read_name(case: Case) -> str:
    return "read:" + case.case + ":" + case.evidence_id


def successors(state: State, cases: Sequence[Case], bounds: Bounds,
               *, broken_conditional: bool = False, faults: bool = True) -> Iterator[tuple[str, State, list[dict]]]:
    """Abstract implementation under test, separate from validate_history.

    A local fresh-admission token is consumed by one provider attempt. Crashes
    erase local state, never durable reservations. Conditional writes are one
    linearization step; response loss is a separate possible result of that step.
    Reads of individual latest pointers are separate scheduling steps.
    """
    def changed(w: int, worker: Worker, target: int | None = None,
                durable: Durable | None = None, **fields: object) -> State:
        workers, records = list(state.workers), list(state.durable)
        workers[w] = worker
        if target is not None and durable is not None:
            records[target] = durable
        return replace(state, workers=tuple(workers), durable=tuple(records), **fields)

    for w, worker in enumerate(state.workers):
        wid = "w" + str(w)
        if worker.pc == "idle":
            if state.deliveries < bounds.deliveries:
                for target, item in enumerate(state.durable):
                    if not item.ack:
                        yield "deliver", changed(w, Worker(target, "admit"), deliveries=state.deliveries + 1), []
            continue
        target, spec = worker.target, cases[worker.target]
        item = state.durable[target]
        if faults and state.crashes < bounds.crashes:
            yield "crash", changed(w, Worker(), crashes=state.crashes + 1), [{"op": "crash", "worker": wid}]
        if worker.pc == "admit":
            if not item.reserved or broken_conditional:
                event = spec.event("admit", worker=wid, fingerprint=spec.fingerprint,
                                   submitted_at=spec.submitted_at, granted=True)
                yield "reserve", changed(w, Worker(target, "provider"), target, replace(item, reserved=True)), [event]
                if faults and state.crashes < bounds.crashes:
                    yield "reserve-response-lost", changed(w, Worker(), target, replace(item, reserved=True),
                                                          crashes=state.crashes + 1), [{**event, "acknowledged": False}]
            else:
                replay = Worker(target, "publish") if item.archived else Worker()
                yield "replay-evidence" if item.archived else "unknown-stop", changed(w, replay), []
        elif worker.pc == "provider":
            event = spec.event("provider", worker=wid)
            yield "provider-return", changed(w, Worker(target, "archive"), target, replace(item, calls=item.calls + 1)), [event]
            if faults and state.crashes < bounds.crashes:
                yield "provider-outcome-unknown", changed(w, Worker(), target, replace(item, calls=item.calls + 1),
                                                         crashes=state.crashes + 1), [event, {"op": "crash", "worker": wid}]
        elif worker.pc == "archive":
            event = spec.event("archive", evidence_id=spec.evidence_id, payload="bytes:" + spec.evidence_id,
                               fingerprint=spec.fingerprint, submitted_at=spec.submitted_at)
            yield "archive", changed(w, Worker(target, "publish"), target, replace(item, archived=True)), [event]
            if faults and state.crashes < bounds.crashes:
                yield "archive-response-lost", changed(w, Worker(), target, replace(item, archived=True),
                                                       crashes=state.crashes + 1), [{**event, "acknowledged": False}, {"op": "crash", "worker": wid}]
        elif worker.pc == "publish":
            if item.phase == 0:
                yield "evidence-marker", changed(w, worker, target, replace(item, phase=1)), [spec.event("phase", phase="evidence")]
            elif not item.receipt:
                yield "receipt", changed(w, worker, target, replace(item, receipt=True)), [spec.event("receipt", status="succeeded", evidence_id=spec.evidence_id)]
            elif not item.candidate:
                yield "candidate", changed(w, worker, target, replace(item, candidate=True)), [spec.event("candidate", evidence_id=spec.evidence_id)]
            elif item.phase == 1:
                yield "candidate-marker", changed(w, worker, target, replace(item, phase=2)), [spec.event("phase", phase="candidates")]
            elif item.phase == 2:
                prior = next((i for i in state.latest if cases[i].case == spec.case), None)
                wins = prior is None or (spec.submitted_at, spec.evidence_id) >= (cases[prior].submitted_at, cases[prior].evidence_id)
                latest = tuple(sorted([i for i in state.latest if cases[i].case != spec.case] + [target])) if wins else state.latest
                events = [spec.event("latest", evidence_id=spec.evidence_id, submitted_at=spec.submitted_at)] if wins else []
                yield "latest", changed(w, Worker(target, "index-list"), latest=latest), events
            elif item.phase == 3:
                yield "digest-start", changed(w, Worker(target, "digest-list")), []
            else:
                yield "ack", changed(w, Worker(), target, replace(item, ack=True)), [spec.event("ack")]
        else:
            kind, step = worker.pc.split("-")
            if step == "list":
                names = tuple(sorted(cases[i].case for i in state.latest))
                event = {"op": "list_latest", "listing": _listing_name(names), "cases": list(names)}
                yield kind + "-list", changed(w, Worker(target, kind + "-read", names, (), getattr(state, kind))), [event]
            elif step == "read":
                if len(worker.snapshot) < len(worker.listing):
                    name = worker.listing[len(worker.snapshot)]
                    pointer = next(i for i in state.latest if cases[i].case == name)
                    record = cases[pointer]
                    event = {"op": "read_latest", "read": _read_name(record), "case": name, "evidence_id": record.evidence_id}
                    yield kind + "-read", changed(w, replace(worker, snapshot=worker.snapshot + (pointer,))), [event]
                else:
                    yield kind + "-ready", changed(w, replace(worker, pc=kind + "-write")), []
            elif step == "write":
                # Independent compare-and-swap abstraction: a competing write
                # after this worker's read invalidates its expected view. This
                # does not make the pointer reads or pointer/view pair atomic.
                if getattr(state, kind) != worker.expected_view:
                    yield kind + "-conditional-conflict", changed(w, Worker(target, kind + "-list")), []
                    continue
                ids = tuple(sorted(cases[i].evidence_id for i in worker.snapshot))
                observation = "observation:" + json.dumps(ids, separators=(",", ":"))
                events = [{"op": "observe", "observation": observation, "listing": _listing_name(worker.listing),
                           "reads": [_read_name(cases[i]) for i in worker.snapshot]},
                          {"op": "view", "kind": kind, "observation": observation, "evidence_set": list(ids)}]
                yield kind + "-write", changed(w, replace(worker, pc=kind + "-verify"), **{kind: tuple(sorted(worker.snapshot))}), events
            elif step == "verify":
                # Bounded-observation abstraction: other workers can change a
                # pointer after this step. A stale snapshot remains legal.
                if tuple(sorted(worker.snapshot)) != state.latest:
                    yield kind + "-retry", changed(w, Worker(target, kind + "-list")), []
                else:
                    phase = 3 if kind == "index" else 4
                    if item.phase < phase:
                        yield kind + "-marker", changed(w, Worker(target, "publish"), target, replace(item, phase=phase)), [spec.event("phase", phase=kind)]
                    else:
                        yield kind + "-already-marked", changed(w, Worker(target, "publish")), []


def equivalence_key(state: State) -> tuple:
    """Forget worker labels, retain all durable facts and local program state.

    Identical workers are interchangeable: reservations have no renewable lease
    or owner authorization in durable state. Local provider tokens remain in pc.
    No evidence identities or submitted-time ordering is quotiented away.
    """
    workers = tuple(sorted((w.target, w.pc, w.listing, w.snapshot, w.expected_view) for w in state.workers))
    return state.durable, workers, state.latest, state.index, state.digest, state.deliveries, state.crashes


@dataclass
class Exploration:
    states: int
    transitions: int
    equivalent_pruned: int
    stutter_pruned: int
    depth_limited: int
    frontier_remaining: int
    truncated: bool
    maximum_depth: int
    bounds: dict
    terminal_states: int = 0
    liveness_projections_checked: int = 0
    liveness_maximum_suffix_steps: int = 0
    violation: Violation | None = None
    counterexample: list[dict] | None = None
    schedule: list[str] | None = None


def explore(cases: Sequence[Case], bounds: Bounds = Bounds(), *, broken_conditional: bool = False,
            check_liveness: bool = False) -> Exploration:
    """Breadth-first enumeration with explicit limits and symmetry pruning."""
    start = initial(cases, bounds)
    queue = deque([(start, [], [], 0)])
    visited = {equivalence_key(start)}
    transitions = equivalent = stutter = depth_limited = max_depth = 0
    violation = counterexample = schedule = None
    liveness_seen: set[tuple] = set()
    liveness_max_steps = terminal = 0
    cut = False
    while queue and not cut:
        state, history, path, depth = queue.popleft()
        max_depth = max(max_depth, depth)
        # The suffix discards worker-local state. Check each distinct durable
        # recovery projection once, without counting worker permutations as new
        # liveness evidence. Unknown/no-evidence reservations are not promises.
        projection = (state.durable, state.latest, state.index, state.digest)
        if check_liveness and any(item.archived for item in state.durable) and projection not in liveness_seen:
            liveness_seen.add(projection)
            _, suffix, report = fair_finish(state, history, cases)
            liveness_max_steps = max(liveness_max_steps, report["steps"])
            if not report["completed"] or not report["provider_counts_unchanged"] or report["safety_violations"]:
                violation = Violation("conditional_liveness", len(suffix), json.dumps(report, sort_keys=True))
                counterexample, schedule, cut = suffix, path + ["fair-replay-suffix"], True
                break
        if depth >= bounds.depth:
            if any(successors(state, cases, bounds, broken_conditional=broken_conditional)):
                depth_limited += 1
            continue
        enabled = list(successors(state, cases, bounds, broken_conditional=broken_conditional))
        if not enabled:
            terminal += 1
        for label, successor, events in enabled:
            transitions += 1
            candidate_history = history + events
            errors = validate_history(candidate_history)
            if errors:
                violation, counterexample, schedule = errors[0], candidate_history, path + [label]
                cut = True
                break
            key = equivalence_key(successor)
            if key == equivalence_key(state):
                stutter += 1
                continue
            if key in visited:
                equivalent += 1
                continue
            if len(visited) >= bounds.states:
                cut = True
                break
            visited.add(key)
            queue.append((successor, candidate_history, path + [label], depth + 1))
    return Exploration(len(visited), transitions, equivalent, stutter, depth_limited, len(queue),
                       bool(queue or depth_limited or (cut and violation is None)), max_depth,
                       vars(bounds), terminal, len(liveness_seen), liveness_max_steps,
                       violation, counterexample, schedule)


def fair_finish(state: State, history: list[dict], cases: Sequence[Case],
                *, step_limit: int = 160) -> tuple[State, list[dict], dict]:
    """Conditional liveness witness using a fair, fault-free replay suffix.

    Assumptions: faults cease; storage operations terminate; latest writes
    eventually stop; each eligible recorded-evidence case is delivered and gets
    publication steps. This is a finite witness, NOT all fair infinite paths.
    Reservations lacking immutable evidence are deliberately outside the target.
    No new provider calls or fresh admissions are permitted in this suffix.
    """
    eligible = tuple(i for i, item in enumerate(state.durable) if item.archived)
    before = tuple(item.calls for item in state.durable)
    # Crash/recovery discards all live local provider tokens, then an explicitly
    # fair replay scheduler offers each evidence-backed case to one worker.
    history = list(history) + [{"op": "crash", "worker": "w" + str(i)} for i in range(len(state.workers))]
    state = replace(state, workers=tuple(Worker() for _ in state.workers))
    steps = 0
    for target in eligible:
        workers = list(state.workers)
        workers[0] = Worker(target, "publish")
        state = replace(state, workers=tuple(workers))
        while not state.durable[target].ack and steps < step_limit:
            choices = [(label, nxt, events) for label, nxt, events in successors(
                state, cases, Bounds(len(state.workers), state.deliveries, state.crashes), faults=False)
                if label != "deliver"]
            if not choices:
                break
            _, state, events = choices[0]
            history.extend(events)
            steps += 1
    errors = validate_history(history)
    complete = all(state.durable[i].ack for i in eligible)
    no_provider = before == tuple(item.calls for item in state.durable)
    return state, history, {"eligible_recorded_cases": len(eligible), "excluded_unknown_cases": sum(
        item.reserved and not item.archived for item in state.durable), "steps": steps,
        "step_limit": step_limit, "completed": complete, "provider_counts_unchanged": no_provider,
        "safety_violations": [vars(error) for error in errors],
        "fairness": "faults cease; finite storage operations; quiescent pointers; fair replay of recorded evidence"}
