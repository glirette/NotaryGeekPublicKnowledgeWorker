"""Run W02 fixtures on the original source plus only the three client injection seams.

Usage: python3 <this-file> [dotnet executable]
A disposable source copy is removed afterward. No branch/ref or live fixture is mutated.
"""
import collections
import io
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
import tempfile
import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parents[2]
original = "d2e5d79eb26167c3e39d2505924a1472a673068d"
dotnet = sys.argv[1] if len(sys.argv) == 2 else "dotnet"
expected = {
    "UnreadableLatestObservationCannotBeReportedAsCompleted": 3,
    "MalformedLatestObservationCannotPublishHealthyEmptyViews": 2,
    "ContradictoryReservationCannotAuthorizeExecutionOrPublication": 5,
    "PreexistingUncertainLegacyParentMustNotFanOut": 1,
    "PointerAdvanceDuringDerivedCommitCannotReturnStaleCompletion": 2,
    "AdmittedLegacyEvidenceCanRecoverInterruptedDerivedPublication": 2,
    "UnreadableOtherLatestCaseKeepsWorkerPublicationPending": 1,
    "ContradictoryQueuedEnvelopeCannotCreateFreshAdmission": 4,
    "RepeatedPointerInterruptionsExhaustBoundWithoutAuthoritativeCompletion": 2,
}
with tempfile.TemporaryDirectory(prefix="w02-negative-") as temporary:
    target = Path(temporary)
    archive = subprocess.run(["git", "archive", original], cwd=repo, check=True, capture_output=True).stdout
    with tarfile.open(fileobj=io.BytesIO(archive)) as stream:
        stream.extractall(target, filter="data")
    tests = "NotaryGeek.PublicKnowledge.Worker.Tests"
    for source in (repo / tests).glob("*.cs"):
        shutil.copy2(source, target / tests / source.name)
    services = Path("NotaryGeek.PublicKnowledge.Worker/Services")
    for name in ("PublicKnowledgePromotionService.cs", "PublicKnowledgeQueueService.cs"):
        shutil.copy2(repo / services / name, target / services / name)
    storage = target / services / "PublicKnowledgeRunStorageService.cs"
    before, after = storage.read_text(), (repo / services / storage.name).read_text()
    start = after.index("    private readonly BlobContainerClient? _injectedContainer;")
    end = after.index("    public PublicKnowledgeRunStorageStatus", start)
    before_start = before.index("    public PublicKnowledgeRunStorageService(")
    before_end = before.index("    public PublicKnowledgeRunStorageStatus", before_start)
    before = before[:before_start] + after[start:end] + before[before_end:]
    header = "    private async Task<BlobContainerClient> GetContainerAsync(CancellationToken cancellationToken)\n    {\n"
    start = after.index(header) + len(header)
    end = after.index("        var connectionString = GetConnectionString();", start)
    before = before.replace(header, header + after[start:end], 1)
    storage.write_text(before)
    environment = dict(os.environ)
    environment.pop("PK_TEST_STORAGE_CONNECTION", None)
    environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    command = [dotnet, "test", "NotaryGeekPublicKnowledgeWorker.slnx", "-m:1", "--nologo", "--filter",
               "FullyQualifiedName~SyntheticQueued|FullyQualifiedName~SyntheticLatest|FullyQualifiedName~LocalStorageSafety",
               "--logger", "trx;LogFileName=w02-negative.trx"]
    result = subprocess.run(command, cwd=target, env=environment, capture_output=True, text=True, timeout=300)
    report = target / tests / "TestResults/w02-negative.trx"
    if not report.exists():
        print(result.stdout[-6000:], result.stderr[-2000:])
        raise SystemExit("Negative-control build/test did not produce a result report.")
    root = ET.parse(report).getroot()
    namespace = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    counters = root.find("t:ResultSummary/t:Counters", namespace).attrib
    failures = collections.Counter(item.attrib["testName"].split("(")[0].rsplit(".", 1)[-1]
                                   for item in root.findall("t:Results/t:UnitTestResult", namespace)
                                   if item.attrib["outcome"] == "Failed")
    if result.returncode != 1 or counters["passed"] != "33" or counters["failed"] != "22" or failures != expected:
        print(result.stdout[-8000:])
        raise SystemExit(f"Unexpected negative-control outcome: {counters}; failing methods: {dict(failures)}")
    print(f"Original {original} plus injection seams: 33 passed; 22 expected regression failures; 0 skipped.")
    print("Matched all nine failing test methods and parameter counts. Disposable copy removed on exit.")
