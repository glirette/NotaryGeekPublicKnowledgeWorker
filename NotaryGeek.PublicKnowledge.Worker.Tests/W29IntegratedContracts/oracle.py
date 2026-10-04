"""Declarative source/effect oracle. Does not authorize or reconstruct reservations."""
import base64,hashlib,json,re
from pathlib import Path
import sys
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'W08ModelChecking'))
from w08_explore import Observer,Violation

def sha(data):return hashlib.sha256(data).hexdigest()
def require(ok,code,detail):
    if not ok:raise Violation(code,detail)

class SourceOracle(Observer):
    def __init__(self, expected):
        super().__init__();self.expected=expected;self.observations={};self.provider=[];self.archived={}
    def prompt(self,case,prompt):
        observed=re.findall(r'--- SOURCE: ([^\n]+)\n([^\n]+)',prompt)
        expected=[(x['final'],x['body']) for x in self.expected[case] if x['ok']]
        require(observed==expected,'wrong-source-reuse',case)
        self.provider.append({'case':case,'sources':[{'url':u,'bodyHash':sha(b.encode())} for u,b in observed]})
    def result(self,value):
        case=value['regressionCaseId'];expected=self.expected[case]
        actual=[(x['url'],x.get('finalUrl'),x['ok']) for x in value['sources']]
        want=[(x['original'],x['final'] if x['ok'] else None,x['ok']) for x in expected]
        require(actual==want,'source-provenance',case+': '+str(actual))
        require(value['sourceCount']==sum(x['ok'] for x in expected),'source-count',case)
    def check(self,authority):
        super().check(authority)
        for name,blob in authority.snapshot().items():
            if name.startswith('runs/') and name[5:9].isdigit():
                v=json.loads(blob.body);self.result(v['result']);self.archived[name]=sha(blob.body)
            if name.startswith('promotion/candidates/'):
                v=json.loads(blob.body)
                allowed={x['final'] for group in self.expected.values() for x in group if x['ok']}
                require(all(x['url'] in allowed for x in v['sources']),'candidate-source','unfetched candidate')
    def settled(self,authority):
        records={n:json.loads(b.body) for n,b in authority.snapshot().items()}
        latest=[v for n,v in records.items() if n.startswith('runs/latest/')]
        if not latest:return
        require('runs/latest-index.json' in records and 'runs/latest-needs-greg.json' in records,'derived-missing','views')
        index=records['runs/latest-index.json'];digest=records['runs/latest-needs-greg.json']
        identities={(v['caseId'],v['storedAtUtc'],v['blobName']) for v in latest}
        require(index['runCount']==len(latest) and {(v['caseId'],v['storedAtUtc'],v['blobName']) for v in index['items']}==identities,'index-source-set','settled pointers')
        require(digest['runCount']==len(latest) and {(v['caseId'],v['storedAtUtc'],v['blobName']) for v in digest['sourceRuns']}==identities,'digest-source-set','settled pointers')
        by_case={v['caseId']:v['result'] for v in latest}
        for item in index['items']:
            result=by_case[item['caseId']]
            require(all(item[k]==result[k] for k in ('ok','status','sourceCount','openAiCalled')),'index-result','archived fields')
            structured=result.get('structuredOutput')
            if structured:
                require(item['summary']==structured['summary'] and item['citations']==structured['citations'],'index-citations','archived output')
        # Full bytes remain in disposable authority; publication evidence records hashes only.
        for v in latest:
            require(v['blobName'] in records,'latest-missing','archive')
        for v in records.values():
            if isinstance(v,dict) and v.get('phase')=='evidence-recorded':
                require(v.get('candidatesPublished') and v.get('indexPublished') and v.get('digestPublished'),'publication-incomplete','settled evidence')
