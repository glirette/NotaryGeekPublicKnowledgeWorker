#!/usr/bin/env python3
"""Coupled actual-process scheduler. W08 owns byte/ETag primitives; W29 adds source identity."""
import argparse,base64,copy,hashlib,itertools,json,re,sys,tempfile,time
from pathlib import Path
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE.parent/'W08ModelChecking'))
from w08_explore import Session,boundary,body_json,get_description,source_binding
from oracle import SourceOracle,require,sha

BASE='https://source.invalid'

def recipe(kind='path'):
    # Expected terminal identities are explicit, not computed by the production redirect algorithm.
    pairs={'path':('/L','/l'),'query':('/?E','/?e'),'slash':('/doc','/doc/'),
           'escape':('/a%2Fb','/a/b'),'alias':('/alias-a','/alias-b'),'different':('/alias-a','/alias-b')}
    paths=pairs.get(kind,('/bad','/good'))
    expected={};routes={};cases=[]
    for i,p in enumerate(paths):
        cid='case-'+chr(97+i);u=BASE+p;final=u;ok=True;body='W29_BODY_'+chr(65+i)+'_v1'
        if kind=='alias':final=BASE+'/shared';body='W29_BODY_SHARED_v1';routes[u]={'status':302,'location':final}
        if kind=='different':final=BASE+('/targetA' if i==0 else '/targetB');routes[u]={'status':307,'location':final}
        if kind in ('blocked','whitespace','malformed','loop','interrupted') and i==0:
            ok=False;final=None
            location={'blocked':'https://blocked.invalid/rejected','whitespace':'   ','malformed':'https://[','loop':'/bad','interrupted':'/pending'}[kind]
            routes[u]={'status':302,'location':location}
            if kind=='interrupted':routes[BASE+'/pending']={'error':'cancel'}
        if ok:routes[final]={'status':200,'body':body}
        expected[cid]=[{'original':u,'final':final,'body':body,'ok':ok}]
        cases.append({'id':cid,'purpose':'public fixture','focus':cid,'mustHold':['source checked'],'failureSignals':[],'sourceUrls':[u]})
    if kind=='selection':
        one=recipe('path');one['cases']=[dict(one['cases'][0],sourceUrls=[BASE+'/L',BASE+'/l'])]
        one['expected']={'case-a':one['expected']['case-a']+one['expected']['case-b']};one['kind']='selection';return one
    return {'kind':kind,'cases':cases,'expected':expected,'routes':routes}

class Coupled(Session):
    def __init__(self,command,state,plan,**kw):
        super().__init__(command,state,{},model_check=False,**kw)
        self.plan=copy.deepcopy(plan);self.observer=SourceOracle(copy.deepcopy(plan['expected']));self.source_events=[];self.effects=[];self.version=1
    def matches_fault(self,c):
        f=self.fault
        if f and f.get('worker')=='*':
            return not self.fired and c.stage==f.get('stage','response') and boundary(c.request)==f['boundary']
        return super().matches_fault(c)
    def advance(self,c):
        if self.matches_fault(c) and c.request['kind']=='source' and self.fault.get('action')=='lost-response':
            # An externally lost source reply supplies no body. This is a fault observation,
            # not an inference from the worker's acceptance or recovery state.
            cid=c.spec['message']['caseId']
            for x in self.observer.expected[cid]:
                if x['original']==c.request['url'] or x['final']==c.request['url']:
                    x['ok']=False;x['final']=None
        return super().advance(c)
    def response_for(self,c):
        r=c.request
        if r['kind']=='source':
            route=self.plan['routes'].get(r['url'])
            require(route is not None,'unexpected-source-fetch',r['url'])
            self.source_events.append({'worker':c.name,'url':r['url'],'version':self.version})
            if 'error' in route:return {'id':r['id'],'error':route['error']}
            headers={'Content-Type':'text/plain'}
            if 'location' in route:headers['Location']=route['location']
            body=route.get('body','W29_REJECTED_BODY')
            return {'id':r['id'],'status':route['status'],'headers':headers,'body':base64.b64encode(body.encode()).decode()}
        if r['kind']=='provider':
            prompt=body_json(r)['input'][0]['content'];case=re.search(r'^Focus: (case-[ab])$',prompt,re.M).group(1)
            self.observer.prompt(case,prompt)
            m=c.spec['message'];identity={'job':m['jobId'],'case':case}
            self.authority.effect(identity);self.effects.append(identity)
            expected=[x for x in self.observer.expected[case] if x['ok']]
            urls=list(dict.fromkeys(x['final'] for x in expected))
            stamp=re.search(r'to exactly ([^;]+);',prompt).group(1)
            candidate={'topicId':case,'title':'Public fixture','summary':'source checked','reviewedAtUtc':stamp,'recheckBeforeUse':True,
                       'sources':[{'url':u,'title':'Fixture','publisher':'Synthetic publisher','kind':'official-agency-page','reviewedAtUtc':stamp,'supports':'source checked'} for u in urls],
                       'supports':['source checked'],'doesNotProve':['no real-service conclusion']}
            output={'summary':'source checked','corrections':[],'sourceConflicts':[],'lawRefreshCandidates':[],
                    'riskFlags':[],'nextActions':[],'attributionNotes':[],'citations':urls,'candidates':[candidate] if urls else []}
            # Use exact production response schema keys from W08's known-valid fixture.
            description=self.description['providerResponse'];template=json.loads(description['output_text'])
            template.update(summary='source checked',citations=urls,candidates=[candidate] if urls else [])
            response=dict(description,output_text=json.dumps(template))
            return {'id':r['id'],'status':200,'headers':{'Content-Type':'application/json'},'body':base64.b64encode(json.dumps(response).encode()).decode()}
        return super().response_for(c)

def execute(command,plan,mode='queued',fault=None,seed=0,policy='round-robin',prefix=(),operations=None,control=None):
    operations=['create','subject','recover'] if operations is None else operations
    with tempfile.TemporaryDirectory(prefix='w29-') as temp:
        s=Coupled(command,temp,plan,seed=seed,policy=policy,fault=fault,prefix=prefix)
        failure=None;outcomes=[];description=get_description(command,{'cases':plan['cases'],'jobId':'job-w29'})
        s.description=description;m=description['message'];cases=description['cases']
        spec=lambda op,msg=m:{'operation':op,'message':msg,'cases':cases}
        messages=[dict(m,caseId=c['id']) for c in cases]
        try:
            if 'create' in operations and mode=='queued':s.single(spec('create'),'setup')
            for c in s.children:c.close()
            s.children=[];s.decisions=[];s.choice_points=[];s.branches=[];s.last=-1
            if 'subject' in operations:
                if mode=='batch':s.start(spec('research-batch'),'w0')
                else:
                    s.start(spec('run',messages[0]),'w0')
                    s.start(spec('run',messages[-1] if len(messages)>1 else messages[0]),'w1')
                    if control=='repeat-effect':
                        s.authority.effect({'job':m['jobId'],'case':cases[0]['id']})
                s.run()
                if fault:require(s.fired==1,'unreached-fault',str(fault))
                if mode=='batch':
                    for c in s.children:
                        require(c.result.get('ok'),'worker-failed',str(c.result))
                        for v in c.result['result']:s.observer.result(v)
            before_fetch=len(s.source_events);before_effect=len(s.authority.effects());before_archives=dict(s.observer.archived)
            if control=='archive-attempt' and before_archives:
                name=next(iter(before_archives));value=json.loads(s.authority.get(name).body)['result']
                value['status']='synthetic-conflicting-result'
                altered=dict(spec('save',messages[0]),result=value)
                c=s.start(altered,'conflicting-save');s.run()
                require(not c.result.get('ok'),'conflicting-archive-accepted','save must reject')
            if control=='archive-replace' and before_archives:
                name=next(iter(before_archives));s.authority.seed(name,b'{"wrong":"replacement"}');s.observer.check(s.authority)
            if 'recover' in operations and mode=='queued':
                # Content changes after initial workers settle. Replay must use archives; reservations without evidence remain uncertain.
                for route in s.plan['routes'].values():
                    if 'body' in route:route['body']=route['body'].replace('_v1','_v2')
                s.version=2;s.fault=None
                for j in range(2):
                    for i,msg in enumerate(messages):
                        c=s.start(spec('run',msg),f'recovery-{j}-{i}');s.run();outcomes.append(c.result)
                        require(c.result.get('ok'),'recovery-failed',str(c.result))
                require(len(s.source_events)==before_fetch,'source-refetched-on-recovery','source changed')
                require(len(s.authority.effects())==before_effect,'provider-during-recovery','effect changed')
                require(s.observer.archived==before_archives,'archive-rewritten','hashes changed')
                s.observer.settled(s.authority)
            s.observer.check(s.authority)
            if mode=='queued' and not fault and 'subject' in operations:
                require(len(s.observer.archived)==len(cases)-(1 if plan.get('kind')=='interrupted' else 0),'missing-evidence','normal completion')
                require(len(s.authority.effects())==len(cases)-(1 if plan.get('kind')=='interrupted' else 0),'missing-effect','normal completion')
            # Derived records must exactly reflect recorded candidates, not current source text.
            snapshot=s.authority.snapshot()
            candidates=[json.loads(b.body) for n,b in snapshot.items() if n.startswith('promotion/candidates/')]
            expected_candidates=[]
            for name in s.observer.archived:
                value=json.loads(snapshot[name].body)['result'];drafts=(value.get('structuredOutput') or {}).get('candidates',[])
                if value['ok'] and 'recover' in operations:
                    expected_candidates.extend(drafts)
                    for draft in drafts:
                        require(any(all(c.get(k)==v for k,v in draft.items()) for c in candidates),'candidate-mismatch',value['regressionCaseId'])
            if 'recover' in operations and mode=='queued':
                require(len(candidates)==len(expected_candidates),'candidate-set','settled candidate count')
        except Exception as ex:failure={'code':getattr(ex,'code',type(ex).__name__),'detail':str(ex)}
        finally:
            result={'kind':plan.get('kind'),'mode':mode,'seed':seed,'policy':policy,'fault':fault,'operations':operations,'control':control,
                    'failure':failure,'steps':s.steps,'faultsFired':s.fired,'decisions':s.decisions,'sourceEvents':s.source_events,
                    'providerObservations':s.observer.provider,'effects':s.authority.effects(),'archiveHashes':s.observer.archived,
                    'events':s.events,'oracleChecks':s.observer.checks,'children':[{'name':c.name,'killed':c.killed,'ok':(c.result or {}).get('ok')} for c in s.children]}
            s.close()
        print(json.dumps({'completed':plan.get('kind'),'mode':mode,'fault':fault,'failure':failure}),flush=True)
        return result

def reduce_operations(command,plan,expected_code,**kw):
    ops=['create','subject','recover'];attempts=[]
    for op in tuple(ops):
        candidate=[x for x in ops if x!=op];r=execute(command,plan,operations=candidate,**kw)
        attempts.append({'operations':candidate,'failure':r['failure']})
        if (r['failure'] or {}).get('code')==expected_code:ops=candidate
    final=execute(command,plan,operations=ops,**kw)
    require((final['failure'] or {}).get('code')==expected_code,'reducer-lost-failure',expected_code)
    # Exhaustive subset comparison independently qualifies deletion minimality over this declared 3-operation domain.
    exhaustive=[]
    for count in range(4):
        for subset in itertools.combinations(['create','subject','recover'],count):
            r=execute(command,plan,operations=list(subset),**kw)
            exhaustive.append({'operations':list(subset),'code':(r['failure'] or {}).get('code')})
    minima=[x for x in exhaustive if x['code']==expected_code]
    require(len(ops)==min(len(x['operations']) for x in minima),'reducer-not-minimal',expected_code)
    return {'operations':ops,'attempts':attempts,'exhaustive':exhaustive,'witness':final}

def binding(worker,root):
    files={}
    for prefix,directory in [('production',root/'NotaryGeek.PublicKnowledge.Worker'),('harness',HERE),('W08',HERE.parent/'W08ModelChecking')]:
        for path in sorted(directory.rglob('*')):
            if path.is_file() and not {'obj','bin','__pycache__','evidence'}.intersection(path.parts) and path.suffix in ('.cs','.fixture','.py','.csproj','.props','.targets'):
                files[prefix+'/'+str(path.relative_to(directory))]=sha(path.read_bytes())
    binaries={path.name:sha(path.read_bytes()) for path in sorted(worker.parent.glob('*')) if path.suffix in ('.dll','.json')}
    value={'files':files,'binaries':binaries};return {'digest':sha(json.dumps(value,sort_keys=True).encode()),**value}

def main():
    p=argparse.ArgumentParser();p.add_argument('--dotnet',required=True);p.add_argument('--worker',type=Path,required=True);p.add_argument('--out',type=Path,required=True)
    p.add_argument('--suite',choices=['smoke','matrix','baseline','controls'],default='smoke');p.add_argument('--source-root',type=Path,default=HERE.parents[1]);p.add_argument('--only',help='One source recipe, for focused replay');p.add_argument('--mutant',choices=['archive','reservation']);a=p.parse_args()
    command=[a.dotnet,str(a.worker.resolve())];initial_binding=binding(a.worker,a.source_root)
    class DurableRuns(list):
        def append(self,value):
            super().append(value)
            with a.out.with_suffix('.partial.jsonl').open('a') as f:
                f.write(json.dumps(value)+'\n');f.flush()
    runs=DurableRuns();reductions=[]
    if a.mutant:
        plan=recipe('path');plan['cases']=plan['cases'][:1];plan['expected']={'case-a':plan['expected']['case-a']}
        kw={'control':'archive-attempt'} if a.mutant=='archive' else {'fault':{'worker':'w0','boundary':'provider','stage':'response','action':'kill'}}
        runs.append(execute(command,plan,**kw))
    elif a.suite=='baseline':
        for kind,mode in [('selection','batch'),('path','batch'),('query','batch'),('whitespace','queued')]:
            plan=recipe(kind);r=execute(command,plan,mode=mode);runs.append(r)
            if r['failure']:reductions.append(reduce_operations(command,plan,r['failure']['code'],mode=mode))
    elif a.suite=='controls':
        for control,code in [('repeat-effect','provider-repeated'),('archive-replace','immutable-overwrite')]:
            plan=recipe('path');runs.append(execute(command,plan,control=control));reductions.append(reduce_operations(command,plan,code,control=control))
    else:
        kinds=[a.only] if a.only else ['path'] if a.suite=='smoke' else ['path','query','slash','escape','alias','different','blocked','whitespace','malformed','loop','interrupted','selection']
        for kind in kinds:
            for policy in (['round-robin'] if a.suite=='smoke' else ['round-robin','reverse']):
                runs.append(execute(command,recipe(kind),policy=policy))
        if a.suite=='matrix':
            for kind in ('path','query','selection'):runs.append(execute(command,recipe(kind),mode='batch'))
            for stage,action in itertools.product(['request','response'],['kill','lost-response']):
                # Lost responses are meaningful only after linearization.
                if stage=='request' and action=='lost-response':continue
                for b in ['source','admission','provider','archive','latest','evidence-phase','receipt','candidate','candidate-phase','index','index-phase','digest','digest-phase']:
                    plan=recipe('path');plan['cases']=plan['cases'][:1];plan['expected']={'case-a':plan['expected']['case-a']}
                    fault={'worker':'*','boundary':b,'stage':stage,'action':action}
                    runs.append(execute(command,plan,fault=fault))
            for seed in (29,2901,0x29C17E):runs.append(execute(command,recipe('different'),seed=seed,policy='random'))
    out={'suite':a.suite,'binding':initial_binding,'sourceChangedDuringRun':initial_binding!=binding(a.worker,a.source_root),'runs':runs,'reductions':reductions,
         'counts':{'runs':len(runs),'failed':sum(bool(x['failure']) for x in runs),'kills':sum(c['killed'] for x in runs for c in x['children'])}}
    a.out.write_text(json.dumps(out,indent=2)+'\n');print(json.dumps(out['counts']))
    for r in runs:
        if r['failure']:print(r['kind'],r['mode'],r['fault'],r['failure'])
    if out['sourceChangedDuringRun']:return 2
    if a.mutant:return 0 if (runs[0]['failure'] or {}).get('code')==('immutable-overwrite' if a.mutant=='archive' else 'provider-repeated') else 1
    if a.suite in ('baseline','controls'):return 0 if all(x['failure'] for x in runs) else 1
    return int(any(x['failure'] for x in runs))
if __name__=='__main__':sys.exit(main())
