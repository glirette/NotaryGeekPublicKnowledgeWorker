#!/usr/bin/env python3
"""Exercise the compiled input harness against disposable composition mutations."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
CORPUS = Path("NotaryGeek.PublicKnowledge.Worker/public-knowledge")
PARTS = Path("evidence/notarycam-worker-2026-10-02")


def run(dotnet, harness, root):
    return subprocess.run([dotnet, harness, str(root)], capture_output=True,
                          text=True, timeout=30)


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")


def update_artifact_binding(package_dir, name):
    path = package_dir / "package.json"
    package = json.loads(path.read_bytes())
    raw = (package_dir / name).read_bytes()
    entry = next(a for a in package["artifacts"] if a["path"] == name)
    entry.update(sha256=hashlib.sha256(raw).hexdigest(), utf8Bytes=len(raw),
                 utf16Characters=len(raw.decode().encode("utf-16-le")) // 2)
    write_json(path, package)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dotnet", required=True)
    parser.add_argument("--harness", required=True)
    args = parser.parse_args()
    dotnet, harness = str(Path(args.dotnet).resolve()), str(Path(args.harness).resolve())
    baseline = run(dotnet, harness, ROOT)
    assert baseline.returncode == 0, baseline.stderr
    assert json.loads(baseline.stdout)["status"] == "passed"
    controls = []
    for control, expected in (
        ("stale-part-binding", "Projection binding differs: notarycam-audit-part-01"),
        ("changed-frame", "Investigation frame changed: notarycam-audit-part-01"),
        ("changed-scope", "Scope changed: notarycam-audit-part-01"),
        ("changed-bytes", "Artifact byte binding differs: safeguards.json"),
    ):
        with tempfile.TemporaryDirectory(prefix="notarycam-projection-") as temp:
            root = Path(temp)
            shutil.copytree(ROOT / CORPUS, root / CORPUS)
            package_dir = root / CORPUS / PARTS
            name = "safeguards.json"
            value = json.loads((package_dir / name).read_bytes())
            if control == "stale-part-binding":
                value["canonicalSha256"] = "0" * 64
            elif control == "changed-frame":
                value["investigationFrame"] = {"primaryIssue": "Synthetic replacement context"}
            elif control == "changed-scope":
                # Exactly one of the two parts carries the source scope.
                name = next(p.name for p in sorted(package_dir.glob("case-01-records-*.json"))
                            if json.loads(p.read_bytes())["context"])
                value = json.loads((package_dir / name).read_bytes())
                value["context"][0]["scope"] = "Synthetic replacement scope"
            else:
                value["useBoundary"] += " Synthetic byte change."
            write_json(package_dir / name, value)
            if control != "changed-bytes":
                # Bypass byte-integrity failure deliberately, to reach the
                # actual parsed-input semantic checks under test.
                update_artifact_binding(package_dir, name)
            result = run(dotnet, harness, root)
            assert result.returncode != 0 and expected in result.stderr, (
                f"{control}: wrong result {result.returncode}: {result.stdout} {result.stderr}")
            controls.append({"control": control, "expectedDiagnostic": expected,
                             "rejected": True})
    print(json.dumps({"status": "passed", "baseline": "passed", "controls": controls}, indent=2))


if __name__ == "__main__":
    main()
