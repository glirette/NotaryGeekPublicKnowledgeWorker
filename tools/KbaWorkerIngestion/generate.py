#!/usr/bin/env python3
"""KBA exact-record adaptation of #38's bounded generation, not a research engine."""
import argparse
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CORPUS = ROOT / 'NotaryGeek.PublicKnowledge.Worker/public-knowledge'
DEST = CORPUS / 'evidence/kba-worker-2026-10-30'
PIN = 'e4c31b4a0b3c2f223978dcb06c6af64f6bb4385d'
INPUTS = {
    'matrix': ('kba-claim-source-matrix-2026-10-30.json', 27486, 'e77ca70072fad0bb1b6e2c3bae0648d73e5fff44e36831e29e62fec15dfa0a0e'),
    'proposal': ('kba-routing-proposal-2026-10-30.json', 13634, '48241103f81e8a28a5281f6c7068e2539c0f92d17b21b997932826d90441a10d'),
}
BASE = 'https://raw.githubusercontent.com/glirette/NotaryGeekPublicKnowledgeWorker/main/NotaryGeek.PublicKnowledge.Worker/public-knowledge/evidence/kba-worker-2026-10-30/'
LIMIT = 17000
BOUNDARY = ('Generated complete-record projection of dated research pending meaning review. Use all three sources together. '
            'Metadata and embedded official URLs are dated references, not current official-source fetches. '
            'Cite the fetched Notary Geek projection as analysis; recheck controlling sources before acting. '
            'Mechanical input qualification is not research approval or model-answer evaluation.')


def encode(value):
    return json.dumps(value, ensure_ascii=False, separators=(',', ':')) + '\n'


def chars(text):
    return len(text.encode('utf-16-le')) // 2


def load_inputs():
    values, bindings = {}, {}
    for key, (name, size, sha) in INPUTS.items():
        raw = (CORPUS / 'research' / name).read_bytes()
        if len(raw) != size or hashlib.sha256(raw).hexdigest() != sha:
            raise ValueError('Pinned research bytes changed: ' + name)
        values[key] = json.loads(raw)
        bindings[key] = {'url': BASE.split('/main/')[0] + '/' + PIN + '/NotaryGeek.PublicKnowledge.Worker/public-knowledge/research/' + name,
                         'sha256': sha, 'utf8Bytes': size}
    return values['matrix'], values['proposal'], bindings


def generate():
    matrix, proposal, bindings = load_inputs()
    # Same largest-complete-record packing as #38. No record slicing, summary,
    # source inference, semantic adjudication, or shared-manifest update.
    fields = ('claims', 'sources', 'contraryInterpretations', 'rules', 'regressions')
    parts = [dict(schema='notary-geek-kba-worker-part-v1', researchBindings=bindings,
                  useBoundary=BOUNDARY, part=i, partCount=3,
                  **{f: [] for f in fields}, matrixContext=[], proposalContext=[]) for i in (1, 2, 3)]
    records = [(f, r) for f in fields for r in (matrix if f in matrix else proposal)[f]]
    records += [('matrixContext', {k: v for k, v in matrix.items() if k not in fields}),
                ('proposalContext', {k: v for k, v in proposal.items() if k not in fields})]
    for field, record in sorted(records, key=lambda r: chars(encode(r[1])), reverse=True):
        min(parts, key=lambda p: chars(encode(p)))[field].append(record)
    for part in parts:
        for field in fields:
            part[field].sort(key=lambda r: r['id'])
        assert chars(encode(part)) <= LIMIT, 'Whole record exceeds bounded package; explicit redesign required'
    names = [f'records-{i}.json' for i in (1, 2, 3)]
    urls = [BASE + n for n in names]
    cases = []
    for rule in proposal['rules']:
        cases.append(dict(id='kba-rule-' + rule['id'].lower(),
                          focus='Review dated KBA routing proposal ' + rule['id'] + ': ' + rule['trigger'] + '. Use all three complete research parts. Preserve qualifications and competing interpretations; do not decide transaction validity.',
                          purpose='Proposed offline regression input; research meaning remains pending review.',
                          mustHold=rule['requiredActions'], failureSignals=rule['mustNotConclude'], sourceUrls=urls))
    for example in proposal['regressions']:
        rules = [r for r in proposal['rules'] if r['id'] in example['ruleIds']]
        cases.append(dict(id='kba-example-' + example['id'].lower(),
                          focus='Review synthetic KBA example ' + example['id'] + ': ' + example['input'] + ' Use all three complete research parts and its named rules. Preserve the expected disposition and meaning as proposed, not an adjudicated result.',
                          purpose='Proposed synthetic meaning regression; input qualification does not evaluate an answer.',
                          mustHold=[example['expectedDisposition'], example['expectedMeaning']] + [a for r in rules for a in r['requiredActions']],
                          failureSignals=[a for r in rules for a in r['mustNotConclude']], sourceUrls=urls))
    files = {n: encode(p) for n, p in zip(names, parts)}
    worker_matrix = dict(schema='notary-geek-public-knowledge-regression-matrix-v1', version='kba-proposal-2026-10-30', reviewedUtc='2026-10-30',
                         purpose='Explicit KBA input proposal; not registered in the shared regression matrix.',
                         publicOnlyPolicy='Public research and synthetic examples only.', cases=cases)
    files['regression-inputs.json'] = json.dumps(worker_matrix, ensure_ascii=False, indent=2) + '\n'
    discovery = dict(schema='notary-geek-kba-worker-discovery-proposal-v1', notApplied=True,
                     status='proposal-only-not-registered', researchBindings=bindings,
                     proposedCaseIds=[c['id'] for c in cases],
                     proposedRegressionInput='regression-inputs.json',
                     proposedSourceUrls=urls,
                     activationBoundary='Future reviewed integration only. Shared manifests, default first-24 selection and runtime limits are unchanged. URLs on main are prospective until publication there; local qualification does not establish HTTP availability or live ingestion.')
    files['discovery-proposal.json'] = json.dumps(discovery, ensure_ascii=False, indent=2) + '\n'
    package = dict(schema='notary-geek-kba-worker-package-v1', researchBindings=bindings,
                   partNames=names, sourceCharacterLimit=LIMIT,
                   coverage=dict(claims=24, sources=22, contraryInterpretations=4, rules=7, examples=22, cases=29, completeResearchPerCase=True),
                   artifacts=[dict(path=n, sha256=hashlib.sha256(v.encode()).hexdigest(), utf8Bytes=len(v.encode()), utf16Characters=chars(v)) for n, v in files.items()])
    files['package.json'] = json.dumps(package, ensure_ascii=False, indent=2) + '\n'
    return files


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    files = generate()
    if args.check:
        assert {p.name for p in DEST.glob('*.json')} == set(files), 'Missing or stale generated JSON'
        for name, content in files.items():
            assert (DEST / name).read_bytes() == content.encode(), 'Stale artifact: ' + name
    else:
        DEST.mkdir(parents=True, exist_ok=True)
        for name, content in files.items():
            (DEST / name).write_bytes(content.encode())
    print(json.dumps(dict(status='verified' if args.check else 'generated', artifacts=len(files), cases=29)))


if __name__ == '__main__':
    main()
