#!/usr/bin/env python3
"""Generate bounded, explicit worker cases from the unmodified dated audit."""
import argparse
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CORPUS = ROOT / "NotaryGeek.PublicKnowledge.Worker/public-knowledge"
SOURCE = CORPUS / "evidence/notarycam-virginia-claims-2026-10-02.json"
DEST = CORPUS / "evidence/notarycam-worker-2026-10-02"
SHA = "84021763580edfcd5c0bcb8560dfbc3e0e4b421c4b9bebc6d6c02c00b5ff14ba"
BASE = "https://raw.githubusercontent.com/glirette/NotaryGeekPublicKnowledgeWorker/main/NotaryGeek.PublicKnowledge.Worker/public-knowledge/evidence/notarycam-worker-2026-10-02/"
PIN = "https://raw.githubusercontent.com/glirette/NotaryGeekPublicKnowledgeWorker/8e186964da07f556168c173e8b1957e525c16a78/NotaryGeek.PublicKnowledge.Worker/public-knowledge/evidence/notarycam-virginia-claims-2026-10-02.json"
LIMIT = 17_000


def encode(value):
    return json.dumps(value, ensure_ascii=False, separators=(",", ":")) + "\n"


def chars(text):
    return len(text.encode("utf-16-le")) // 2


def generate():
    raw = SOURCE.read_bytes()
    assert len(raw) == 167039 and hashlib.sha256(raw).hexdigest() == SHA, "Canonical audit changed; review/regenerate a new dated package explicitly."
    audit = json.loads(raw)
    common = {
        "schema": "notary-geek-audit-worker-part-v1",
        "auditId": audit["auditId"],
        "reviewedOn": audit["reviewedOn"],
        "canonicalSnapshot": PIN,
        "canonicalSha256": SHA,
        "useBoundary": "Generated exact-record projection, not independently updated authority. Use all three explicit case sources together. Source metadata and URLs are dated references, not proof this job fetched current official law. Cite the fetched projection as Notary Geek analysis; recheck controlling sources before acting. A job covers only its named claim IDs, never the entire audit or every transaction.",
    }
    safeguards = dict(common, kind="shared-safeguards", interpretationRules=audit["interpretationRules"], limitations=audit["limitations"], investigationFrame=audit["investigationFrame"])
    assert chars(encode(safeguards)) <= LIMIT
    sources = {s["id"]: s for s in audit["sources"]}
    all_rule_sources = {s for r in audit["interpretationRules"]["rules"] for s in r["sourceIds"]}
    all_claims = {c["id"]: c for c in audit["claims"]}

    def parts(claims, number):
        ids = {c["id"] for c in claims}
        # Preserve defenses relevant to an overlap finding, too, without asserting
        # that the referenced finding itself is part of this case's coverage.
        related = ids | {i for c in claims for i in c.get("overlapsWith", [])}
        counterarguments = [c for c in audit["counterarguments"] if related.intersection(c["relatedClaimIds"])]
        source_ids = all_rule_sources | {s for c in claims + counterarguments for s in c["sourceIds"]}
        records = [("claims", c) for c in claims]
        records += [("counterarguments", c) for c in counterarguments]
        records += [("sources", s) for s in audit["sources"] if s["id"] in source_ids]
        records += [("context", {"scope": audit["scope"]})]
        bins = [dict(common, kind="case-records", caseId=f"notarycam-audit-part-{number:02}", claimIds=[c["id"] for c in claims], part=i, partCount=2, claims=[], counterarguments=[], sources=[], context=[]) for i in (1, 2)]
        # Largest complete records first: no source, claim or safeguard is cut.
        for field, record in sorted(records, key=lambda r: chars(encode(r[1])), reverse=True):
            target = min(bins, key=lambda b: chars(encode(b)))
            target[field].append(record)
        for b in bins:
            for field in ("claims", "counterarguments", "sources"):
                b[field].sort(key=lambda r: r["id"])
        return bins if all(chars(encode(b)) <= LIMIT for b in bins) else None

    groups, current = [], []
    for claim in audit["claims"]:
        if current and parts(current + [claim], len(groups) + 1) is None:
            groups.append(current)
            current = []
        current.append(claim)
        assert parts(current, len(groups) + 1), f"Single claim exceeds bound: {claim['id']}"
    if current:
        groups.append(current)
    files = {"safeguards.json": encode(safeguards)}
    cases = []
    for number, claims in enumerate(groups, 1):
        names = [f"case-{number:02}-records-{part}.json" for part in (1, 2)]
        for name, part in zip(names, parts(claims, number)):
            files[name] = encode(part)
        ids = [c["id"] for c in claims]
        cases.append({
            "id": f"notarycam-audit-part-{number:02}",
            "focus": "Review only dated NotaryCam audit findings " + ", ".join(ids) + ". Combine both record parts and all shared interpretation safeguards. Preserve each classification, correction, evidence boundary and source scope. Do not mistake the source catalog for freshly fetched official law or this part for complete audit coverage.",
            "purpose": "Explicit bounded ingestion of complete dated claim records and all interpretation rules; discovery in the default manifest is not ingestion evidence.",
            "mustHold": [
                "Preserve each supplied finding's classification, correction, dated source scope and evidence boundary.",
                "Apply all supplied interpretation safeguards; an unrelated unknown does not erase an established finding.",
                "Distinguish Notary Geek analysis, provider representation and controlling official authority.",
                "Limit coverage to this case's named claims and require current-source rechecking before acting."
            ],
            "failureSignals": [
                "Invents a completed-session defect, knowing intent, unobserved logo permission decision or universal provider exoneration.",
                "Treats source metadata as a current official-source fetch or one case as coverage of all 83 findings.",
                "Uses badge, market share, hypothetical controls or absence of observed injury to erase a sourced contradiction."
            ],
            "sourceUrls": [BASE + "safeguards.json"] + [BASE + n for n in names]
        })
    manifest = {
        "schema": "notary-geek-audit-worker-package-v1", "auditId": audit["auditId"],
        "canonicalSnapshot": PIN, "canonicalSha256": SHA,
        "selection": "Explicit regression cases only. The default manifest's first-24 selection is unchanged and does not select this audit.",
        "coverage": {"claims": len(all_claims), "interpretationRulesPerCase": 15, "limitationsPerCase": len(audit["limitations"]), "cases": len(cases), "sourceCharacterLimit": LIMIT},
        "cases": [{"id": c["id"], "claimIds": [x["id"] for x in g], "sourceUrls": c["sourceUrls"]} for c, g in zip(cases, groups)],
        "artifacts": [{"path": n, "sha256": hashlib.sha256(v.encode()).hexdigest(), "utf8Bytes": len(v.encode()), "utf16Characters": chars(v)} for n, v in files.items()]
    }
    files["package.json"] = json.dumps(manifest, ensure_ascii=False, indent=2) + "\n"
    return files, cases


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    files, cases = generate()
    matrix_path = CORPUS / "public-knowledge-regression-matrix.json"
    matrix = json.loads(matrix_path.read_bytes())
    preserved = [c for c in matrix["cases"] if not c["id"].startswith("notarycam-audit-part-")]
    if args.check:
        assert DEST.exists()
        assert {p.name for p in DEST.glob("*.json")} == set(files), "Missing or stale generated artifact"
        for name, content in files.items():
            assert (DEST / name).read_bytes() == content.encode(), f"Stale artifact: {name}"
        assert matrix["cases"] == preserved + cases, "Regression source contract differs from generated cases"
    else:
        DEST.mkdir(parents=True, exist_ok=True)
        for old in DEST.glob("*.json"):
            if old.name not in files:
                old.unlink()
        for name, content in files.items():
            (DEST / name).write_bytes(content.encode())
        matrix["cases"] = preserved + cases
        matrix_path.write_text(json.dumps(matrix, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"status": "verified" if args.check else "generated", "cases": len(cases), "claims": 83, "artifacts": len(files), "canonicalSha256": SHA}))


if __name__ == "__main__":
    main()
