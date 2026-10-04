"""Cross-version immutable replay: original W08 process produces evidence, combined process recovers."""
import argparse,base64,json,sys,tempfile
from pathlib import Path
from explore import Coupled,recipe,get_description,require,sha
from w08_explore import Session,completed
p=argparse.ArgumentParser();p.add_argument('--dotnet',required=True);p.add_argument('--worker',required=True);p.add_argument('--out',type=Path,required=True);p.add_argument('--origin',default='5cfa849c00ea31cd5e5cc8a7c2cf1bb99d9be6bb');p.add_argument('--read',type=Path);a=p.parse_args()
command=[a.dotnet,a.worker]
with tempfile.TemporaryDirectory(prefix='w29-history-') as d:
    if a.read:
        saved=json.loads(a.read.read_text());desc=saved['description'];s=Session(command,d,desc['providerResponse'],model_check=False)
        for name,b in saved['blobs'].items():s.authority.seed(name,base64.b64decode(b))
        before={n:sha(b.body) for n,b in s.authority.snapshot().items() if n.startswith('runs/') and n[5:9].isdigit()}
    else:
        # Original unmodified W08 worker only permits its source fixture URL.
        desc=get_description(command);s=Session(command,d,desc['providerResponse'],model_check=False);before={}
    m=desc['message'];spec={'operation':'run','message':m,'cases':desc['cases']}
    try:
        if not a.read:
            s.single(dict(spec,operation='create'),'setup')
            s.fault={'worker':'old','boundary':'candidate','stage':'response','action':'kill'}
            s.start(spec,'old');s.run();require(s.fired==1,'history-boundary','candidate')
        else:
            s.single(spec,'combined-recover')
            require(not s.authority.effects(),'historical-provider-repeat','effects')
            require(not any(e.get('kind')=='source' for e in s.events),'historical-refetch','source')
            after={n:sha(b.body) for n,b in s.authority.snapshot().items() if n.startswith('runs/') and n[5:9].isdigit()}
            require(before==after,'historical-bytes-changed','archives')
            require(completed(s.authority,m),'historical-recovery-incomplete','job must complete publication')
        result={'origin':saved['origin'] if a.read else a.origin,'description':desc,
                'blobs':{n:base64.b64encode(b.body).decode() for n,b in s.authority.snapshot().items()},
                'archiveHashes':{n:sha(b.body) for n,b in s.authority.snapshot().items() if n.startswith('runs/') and n[5:9].isdigit()},
                'events':s.events,'effects':s.authority.effects(),'mode':'recover' if a.read else 'original'}
        a.out.write_text(json.dumps(result,indent=2)+'\n');print(result['mode'],result['archiveHashes'])
    finally:s.close()
