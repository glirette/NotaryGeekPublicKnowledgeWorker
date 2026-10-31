#!/usr/bin/env python3
"""Causal controls against the real compiled production-method harness (#38 pattern)."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]
CORPUS = Path('NotaryGeek.PublicKnowledge.Worker/public-knowledge')
PACKAGE = CORPUS / 'evidence/kba-worker-2026-10-30'
PRODUCTION = [
    'Services/PublicKnowledgeResearchService.cs', 'Services/PublicKnowledgeProviderOutput.cs',
    'Services/PublicKnowledgeExecutionPolicy.cs', 'Models/PublicKnowledgeModels.cs',
    'Configuration/PublicKnowledgeOptions.cs', 'Configuration/OpenAiOptions.cs',
    'Configuration/StraicoOptions.cs',
]


def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, separators=(',', ':')) + '\n', encoding='utf-8')


def rebind(directory, name):
    package = json.loads((directory / 'package.json').read_bytes())
    raw = (directory / name).read_bytes()
    entry = next(a for a in package['artifacts'] if a['path'] == name)
    entry.update(sha256=hashlib.sha256(raw).hexdigest(), utf8Bytes=len(raw),
                 utf16Characters=len(raw.decode().encode('utf-16-le')) // 2)
    write(directory / 'package.json', package)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--dotnet', required=True)
    parser.add_argument('--harness', required=True)
    args = parser.parse_args()
    dotnet, harness = str(Path(args.dotnet).resolve()), str(Path(args.harness).resolve())

    def run(root):
        return subprocess.run([dotnet, harness, str(root)], capture_output=True, text=True, timeout=30)

    baseline = run(ROOT)
    assert baseline.returncode == 0, baseline.stderr
    assert json.loads(baseline.stdout)['status'] == 'passed'
    controls = []
    for control, diagnostic in [
        ('research-matrix-byte-drift', 'Pinned research bytes changed: kba-claim-source-matrix'),
        ('research-proposal-byte-drift', 'Pinned research bytes changed: kba-routing-proposal'),
        ('generated-byte-drift', 'Artifact byte binding differs: records-1.json'),
        ('stale-part-binding', 'Research binding differs'),
        ('omitted-claim-qualification', 'Complete record differs: claims/K09'),
        ('wrong-valid-source-binding', 'Complete record differs: claims/K01'),
        ('changed-source-date', 'Complete record differs: sources/nist-a4'),
        ('omitted-refresh-qualification', 'Complete context differs: matrix'),
        ('changed-competing-interpretation', 'Complete record differs: contraryInterpretations/C04'),
        ('changed-expected-meaning', 'Complete record differs: regressions/E04'),
        ('changed-worker-expectation', 'Worker expected meaning differs: kba-example-e04'),
        ('wrong-case-sources', 'Case source selection differs: kba-rule-r01'),
        ('discovery-activation', 'Discovery must remain unapplied'),
    ]:
        with tempfile.TemporaryDirectory(prefix='kba-input-control-') as tmp:
            root = Path(tmp)
            shutil.copytree(ROOT / CORPUS, root / CORPUS)
            for name in PRODUCTION:
                destination = root / 'NotaryGeek.PublicKnowledge.Worker' / name
                destination.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(ROOT / 'NotaryGeek.PublicKnowledge.Worker' / name, destination)
            directory = root / PACKAGE
            refreshed = False
            if control.startswith('research-'):
                name = 'kba-claim-source-matrix-2026-10-30.json' if 'matrix' in control else 'kba-routing-proposal-2026-10-30.json'
                path = root / CORPUS / 'research' / name
                path.write_bytes(path.read_bytes() + b'\n')
            elif control == 'generated-byte-drift':
                path = directory / 'records-1.json'
                path.write_bytes(path.read_bytes() + b' ')
            else:
                parts = {p.name: json.loads(p.read_bytes()) for p in sorted(directory.glob('records-*.json'))}

                def record(field, identity):
                    for name, part in parts.items():
                        for row in part[field]:
                            if row['id'] == identity:
                                return name, part, row
                    raise AssertionError('Missing control target')

                if control == 'stale-part-binding':
                    name = 'records-1.json'; value = parts[name]
                    value['researchBindings']['matrix']['sha256'] = '0' * 64
                elif control == 'omitted-claim-qualification':
                    name, value, row = record('claims', 'K09'); del row['evidenceLimit']
                elif control == 'wrong-valid-source-binding':
                    name, value, row = record('claims', 'K01'); row['sourceIds'] = ['proof-kba']
                elif control == 'changed-source-date':
                    name, value, row = record('sources', 'nist-a4'); row['checkedDate'] = '2026-10-31'
                elif control == 'omitted-refresh-qualification':
                    name, value = next((n, p) for n, p in parts.items() if p['matrixContext'])
                    value['matrixContext'][0]['sourceRefreshLimits'] = []
                elif control == 'changed-competing-interpretation':
                    name, value, row = record('contraryInterpretations', 'C04'); row['concession'] = 'Synthetic removed concession'
                elif control == 'changed-expected-meaning':
                    name, value, row = record('regressions', 'E04'); row['expectedMeaning'] = 'Synthetic reversed eligibility meaning'
                elif control.startswith('changed-worker') or control == 'wrong-case-sources':
                    name = 'regression-inputs.json'; value = json.loads((directory / name).read_bytes())
                    if control == 'wrong-case-sources':
                        value['cases'][0]['sourceUrls'] = value['cases'][0]['sourceUrls'][:2]
                    else:
                        next(c for c in value['cases'] if c['id'] == 'kba-example-e04')['mustHold'][1] = 'Synthetic reversed eligibility meaning'
                elif control == 'discovery-activation':
                    name = 'discovery-proposal.json'; value = json.loads((directory / name).read_bytes()); value['notApplied'] = False
                else:
                    raise AssertionError(control)
                write(directory / name, value)
                # Deliberately refresh bytes, so semantic-record/binding checks
                # must reject the defect after real fetch and prompt building.
                rebind(directory, name); refreshed = True
            result = run(root)
            assert result.returncode != 0 and diagnostic in result.stderr, (control, result.returncode, result.stdout, result.stderr)
            controls.append(dict(control=control, rejected=True, expectedDiagnostic=diagnostic,
                                 artifactChecksumsRefreshed=refreshed))
    print(json.dumps(dict(status='passed', baseline='passed', controls=controls), indent=2))


if __name__ == '__main__':
    main()
