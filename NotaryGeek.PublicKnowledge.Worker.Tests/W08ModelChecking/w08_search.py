#!/usr/bin/env python3
"""Bounded concrete prefix search using the independent model as an online oracle.

Only branch opposite choices when two pending HTTP operations touch the same
object and at least one writes. Distinct objects/read-only pairs are pruned for
THIS prefix generator, not asserted globally equivalent distributed executions.
Explicit provider/response-loss/kill schedules are explored by w08_explore.py.
"""
import argparse
import json
from pathlib import Path
from w08_explore import ROOT, get_description, run_scenario, source_binding


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--dotnet", default="dotnet")
    p.add_argument("--worker", type=Path, default=ROOT / "NotaryGeek.PublicKnowledge.Worker.Tests/bin/W08Worker/Debug/net10.0/W08Worker.dll")
    p.add_argument("--out", type=Path, required=True)
    p.add_argument("--max-schedules", type=int, default=12)
    p.add_argument("--prefix-depth", type=int, default=70)
    args = p.parse_args()
    if not 1 <= args.max_schedules <= 100 or not 1 <= args.prefix_depth <= 200:
        p.error("bounds exceed supported local budget")
    command = [args.dotnet, "exec", str(args.worker)]
    description = get_description(command)
    binding = source_binding(args.worker)
    args.out.mkdir(parents=True, exist_ok=True)
    queue, seen = [()], {()}
    results, ignored, duplicate_prefixes = [], 0, 0
    while queue and len(results) < args.max_schedules:
        prefix = queue.pop(0)
        result = run_scenario(command, description, "duplicate", policy="round-robin", prefix=prefix)
        record = {"source_binding": binding, "scenario": "duplicate", "policy": "round-robin",
                  "prefix": prefix, "result": result}
        (args.out / f"search-{len(results):03d}.json").write_text(json.dumps(record, indent=2) + "\n")
        results.append({"actions": result["actions"], "failure": result["failure"], "oracle_checks": result["oracle_checks"]})
        for index, choices in enumerate(result["choice_points"][:args.prefix_depth]):
            if len(choices) < 2:
                continue
            a, b = choices[:2]
            conflict = a["object"] == b["object"] and a["boundary"] != "other" and (
                a["method"] in ("PUT", "POST") or b["method"] in ("PUT", "POST"))
            if not conflict:
                ignored += 1
                continue
            for choice in choices:
                if choice["worker"] == result["decisions"][index]:
                    continue
                candidate = tuple(result["decisions"][:index] + [choice["worker"]])
                if candidate in seen:
                    duplicate_prefixes += 1
                else:
                    seen.add(candidate)
                    queue.append(candidate)
        print(json.dumps({"schedule": len(results), **results[-1]}), flush=True)
    report = {"source_binding": binding, "source_changed_during_run": binding != source_binding(args.worker),
              "schedules": len(results), "failed": sum(bool(r["failure"]) for r in results),
              "max_schedules": args.max_schedules, "prefix_depth": args.prefix_depth,
              "duplicate_prefixes_pruned": duplicate_prefixes, "nonconflicting_choice_pairs_not_branched": ignored,
              "pending_prefixes": len(queue), "truncated": bool(queue), "results": results,
              "limit": "bounded prefix search; pruning is local HTTP independence, not exhaustive distributed equivalence"}
    (args.out / "summary.json").write_text(json.dumps(report, indent=2) + "\n")
    return bool(report["failed"] or report["source_changed_during_run"])


if __name__ == "__main__":
    raise SystemExit(main())
