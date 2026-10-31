#!/usr/bin/env python3
"""Offline research integrity; does not evaluate model answers or transactions."""
from copy import deepcopy
import json
from pathlib import Path
import re
import sys
from urllib.parse import urlparse

ROOT = Path(__file__).resolve().parents[2]
RESEARCH = ROOT / 'NotaryGeek.PublicKnowledge.Worker/public-knowledge/research'
DATE = '2026-10-30'


def unique(rows):
    ids = [row['id'] for row in rows]
    assert len(ids) == len(set(ids)), 'duplicate ID'
    return set(ids)


def validate(matrix, proposal, discovery):
    assert matrix['status'] == 'public-research-draft-for-greg-meaning-review'
    assert proposal['status'] == 'proposal-only-not-registered'
    assert discovery['notApplied'] is True
    assert matrix['checkedDate'] == proposal['checkedDate'] == discovery['checkedDate'] == DATE
    sources, claims, rules = unique(matrix['sources']), unique(matrix['claims']), unique(proposal['rules'])
    unique(proposal['regressions'])
    source_map = {s['id']: s for s in matrix['sources']}
    statuses = {'documented-requirement', 'documented-risk', 'attributed-product-claim',
                'documented-limitation', 'editorial-inference', 'source-conflict',
                'documented-law', 'unresolved', 'documented-product-scope',
                'publisher-stated-offering', 'editorial-correction', 'source-distinction'}
    for source in matrix['sources']:
        assert urlparse(source['url']).scheme == 'https'
        assert all(source.get(k) for k in ('locator', 'version', 'authorityBoundary', 'retrieval'))
        assert source['checkedDate'] == DATE
    for claim in matrix['claims']:
        assert claim['epistemicStatus'] in statuses
        assert claim['sourceIds'] and set(claim['sourceIds']) <= sources
        assert claim['evidenceLimit'] and claim['proposedMeaning'] and claim['claim']
        if claim['epistemicStatus'] == 'documented-law':
            assert all(source_map[x]['type'] in {'enacted-law', 'codified-law', 'constitution'}
                       for x in claim['sourceIds']), 'non-law source promoted to law'
    for rule in proposal['rules']:
        assert rule['claimIds'] and set(rule['claimIds']) <= claims
        assert rule['requiredActions'] and rule['mustNotConclude']
    for case in proposal['regressions']:
        assert case['ruleIds'] and set(case['ruleIds']) <= rules
        assert case['synthetic'] is True
        assert case['input'] and case['expectedMeaning'] and case['expectedDisposition']
    coverage = {case['expectedDisposition'] for case in proposal['regressions']}
    assert {'reject-equivalence', 'reject-universal-ban', 'address-condition-unmet',
            'missing-method-mapping', 'wrong-certificate-subject', 'transaction-date-law',
            'no-question-generation', 'scoring-source-conflict', 'supplier-unresolved',
            'potentially-permitted-role', 'validity-unresolved', 'conditional-service-only',
            'alternative-needs-assessment', 'reuse-policy-vs-observation'} <= coverage
    assert {'legal-permission', 'technical-assurance', 'provider-configuration',
            'recipient-requirements', 'service-boundary'} <= {c['layer'] for c in matrix['claims']}
    measured = matrix['measuredFacts']
    assert measured['transactionSamples'] == measured['liveProviderTransactions'] == 0
    assert measured['empiricalFailureRate'] is measured['empiricalAttackSuccessRate'] is None
    for counter in matrix['contraryInterpretations']:
        assert counter['sourceIds'] and set(counter['sourceIds']) <= sources
        assert counter['concession'] and counter['response']
    assert len(matrix['contraryInterpretations']) >= 4
    assert len(proposal['alternativeMethodEvidence']) >= 4
    assert {m['item'] for m in proposal['virginiaNoKbaListedMethods']} == {1, 2, 3, 4}


def main():
    matrix = json.loads((RESEARCH / f'kba-claim-source-matrix-{DATE}.json').read_text())
    proposal = json.loads((RESEARCH / f'kba-routing-proposal-{DATE}.json').read_text())
    discovery = json.loads((RESEARCH / f'kba-discovery-delta-proposal-{DATE}.json').read_text())
    validate(matrix, proposal, discovery)
    checked = 0
    for path in sorted(RESEARCH.glob('kba-*')):
        if not path.is_file():
            continue
        text = path.read_text()
        assert text.endswith('\n') and '\r' not in text
        assert all(line == line.rstrip() for line in text.splitlines()), path.name
        if path.suffix == '.json':
            json.loads(text)
        if path.suffix == '.md':
            for target in re.findall(r'\]\(([^)]+)\)', text):
                if not urlparse(target).scheme and not target.startswith('#'):
                    assert (path.parent / target.split('#')[0]).is_file(), target
        checked += 1
    mutations = []
    x = deepcopy(matrix); x['claims'][0]['sourceIds'] = ['missing-source']
    mutations.append(('dangling-source', x, proposal, discovery))
    x = deepcopy(matrix); x['claims'][0]['epistemicStatus'] = 'documented-law'
    mutations.append(('standard-promoted-to-law', x, proposal, discovery))
    x = deepcopy(matrix); x['measuredFacts']['empiricalFailureRate'] = 0.5
    mutations.append(('invented-rate', x, proposal, discovery))
    x = deepcopy(proposal); x['regressions'] = [c for c in x['regressions']
                                              if c['expectedDisposition'] != 'address-condition-unmet']
    mutations.append(('lost-address-case', matrix, x, discovery))
    x = deepcopy(proposal); x['regressions'][0]['ruleIds'] = ['missing-rule']
    mutations.append(('dangling-rule', matrix, x, discovery))
    x = deepcopy(discovery); x['notApplied'] = False
    mutations.append(('activation-claim', matrix, proposal, x))
    for name, m, p, d in mutations:
        try:
            validate(m, p, d)
        except AssertionError:
            continue
        raise AssertionError(f'negative control accepted: {name}')
    print(f"PASS: {len(matrix['sources'])} sources, {len(matrix['claims'])} claims, "
          f"{len(proposal['rules'])} proposed rules, {len(proposal['regressions'])} semantic examples; "
          f"{checked} research files; {len(mutations)} rejected mutation controls.")
    print('Integrity/coverage only: no live model, browser, provider or legal-outcome evaluation.')


if __name__ == '__main__':
    try:
        main()
    except (AssertionError, KeyError, ValueError) as exc:
        print(f'FAIL: {exc}', file=sys.stderr)
        raise SystemExit(1)
