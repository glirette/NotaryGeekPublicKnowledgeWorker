#!/usr/bin/env python3
"""Rebuild only the two repaired production files at W08's starting revision.

The temporary copy retains the current harness and unchanged dependency files.
No live configuration is loaded; this does not start Azurite or application hosts.
It preserves the working checkout and compares named production failure codes.
"""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
ORIGINAL = "27097b551b75d2d64a9e3f1df26c61e73a3eb792"
PATHS = ["NotaryGeek.PublicKnowledge.Worker/Functions/PublicKnowledgeResearchFunction.cs",
         "NotaryGeek.PublicKnowledge.Worker/Services/PublicKnowledgeRunStorageService.cs"]
EXPECTED = {"unknown-child": "unknown-child-mutated-job", "legacy-catalog-casing": "provider-repeated",
            "missing-failed-catalog": "completion-before-phases",
            "fingerprint-order": "equivalent-map-recovery-blocked"}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--dotnet", default="dotnet")
    p.add_argument("--out", type=Path, required=True)
    args = p.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    dotnet = str(Path(args.dotnet).resolve()) if "/" in args.dotnet else args.dotnet
    env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER="1",
               DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE="true")
    with tempfile.TemporaryDirectory(prefix="w08-original-") as directory:
        target = Path(directory)
        shutil.copytree(ROOT / "NotaryGeek.PublicKnowledge.Worker", target / "NotaryGeek.PublicKnowledge.Worker",
                        ignore=shutil.ignore_patterns("bin", "obj", "local.settings.json", "App_Data"))
        harness = target / "NotaryGeek.PublicKnowledge.Worker.Tests/W08ModelChecking"
        shutil.copytree(HERE, harness, ignore=shutil.ignore_patterns("__pycache__", "evidence"))
        for path in PATHS:
            (target / path).write_bytes(subprocess.check_output(["git", "show", f"{ORIGINAL}:{path}"], cwd=ROOT))
        build = [dotnet, "build", str(harness / "W08Worker.csproj"), "-m:1", "--nologo",
                 "--disable-build-servers", "-nodeReuse:false", "-p:UseSharedCompilation=false",
                 "-p:NuGetAudit=false", "--ignore-failed-sources"]
        with (args.out / "build.log").open("w") as output:
            subprocess.run(build, cwd=target, env=env, stdout=output, stderr=subprocess.STDOUT, check=True, timeout=180)
        worker = target / "NotaryGeek.PublicKnowledge.Worker.Tests/bin/W08Worker/Debug/net10.0/W08Worker.dll"
        command = [sys.executable, str(harness / "w08_regressions.py"), "--dotnet", dotnet,
                   "--worker", str(worker), "--out", str(args.out.resolve()), "--minimize", "--expect-original-failures"]
        subprocess.run(command, cwd=target, env=env, check=True, timeout=180)
        summary = json.loads((args.out / "summary.json").read_text())
        observed = {r["case"]: (r["failure"] or {}).get("code") for r in summary["results"]}
        if observed != EXPECTED:
            raise AssertionError(f"Original-source failure classification differs: {observed}")
        summary["original_production_head"] = ORIGINAL
        summary["replaced_paths"] = PATHS
        summary["exact_negative_controls_verified"] = True
        (args.out / "summary.json").write_text(json.dumps(summary, indent=2) + "\n")
    print("Four original-source negative controls reproduced and minimized; all temporary workers reaped.")


if __name__ == "__main__":
    main()
