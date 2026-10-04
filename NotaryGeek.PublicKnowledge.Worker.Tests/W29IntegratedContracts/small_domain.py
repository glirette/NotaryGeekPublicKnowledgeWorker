"""Exhaust all six interleavings of two pending source request/reply pairs in production.
Pruning is qualified only for this four-action domain, not the entire pipeline.
"""
import argparse,itertools,json,tempfile
from pathlib import Path
from explore import Coupled,recipe,get_description,require,binding
from w08_explore import boundary
p=argparse.ArgumentParser();p.add_argument('--dotnet',required=True);p.add_argument('--worker',type=Path,required=True);p.add_argument('--out',type=Path,required=True);a=p.parse_args()
command=[a.dotnet,str(a.worker.resolve())];plans=recipe('path');description=get_description(command,{'cases':plans['cases'],'jobId':'job-small'})
traces=[t for t in itertools.permutations(('a-send','a-reply','b-send','b-reply')) if t.index('a-send')<t.index('a-reply') and t.index('b-send')<t.index('b-reply')]
results=[]
for trace in traces:
 with tempfile.TemporaryDirectory(prefix='w29-small-') as d:
  s=Coupled(command,d,plans);s.description=description;m=description['message'];cases=description['cases'];spec=lambda op,msg:{'operation':op,'message':msg,'cases':cases}
  try:
   s.single(spec('create',m),'setup');workers={}
   for i,key in enumerate(('a','b')):
    c=s.start(spec('run',dict(m,caseId=cases[i]['id'])),key);workers[key]=c
    while True:
     s.await_boundaries()
     require(c.stage!='done','source-not-reached',key)
     if c.stage=='request' and boundary(c.request)=='source':break
     s.advance(c)
   for action in trace:
    key,step=action.split('-');c=workers[key]
    require(c.stage==('request' if step=='send' else 'response'),'small-stage',action);s.advance(c)
   s.run()
   for i,key in enumerate(('a','b')):s.single(spec('run',dict(m,caseId=cases[i]['id'])),'recover-'+key)
   s.observer.settled(s.authority)
   result={'trace':trace,'projection':sorted(s.observer.provider,key=lambda x:x['case']),'events':s.events,'archiveHashes':s.observer.archived}
   results.append(result)
  finally:s.close()
projections={json.dumps(r['projection'],sort_keys=True) for r in results}
require(len(results)==6 and len(projections)==1,'pruning-disagreement','two pending source pairs')
a.out.write_text(json.dumps({'domain':'two source request/reply pairs; other actions deterministically drained','exhaustive':6,'prunedRepresentatives':1,'equalSourceEffectProjections':True,'runs':results,'binding':binding(a.worker,Path(__file__).resolve().parents[2])},indent=2)+'\n')
print('6 exhaustive concrete schedules; one equal source/effect projection; pruning qualified only for this domain')
