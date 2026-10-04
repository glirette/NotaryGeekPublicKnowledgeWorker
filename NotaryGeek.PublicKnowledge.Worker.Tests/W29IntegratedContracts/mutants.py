"""Create precisely specified production negative controls in NEW scratch copies only."""
import argparse,hashlib,json,pathlib,shutil
p=argparse.ArgumentParser();p.add_argument('--out',type=pathlib.Path,required=True);p.add_argument('--mutant',choices=['archive','reservation'],required=True);a=p.parse_args()
root=pathlib.Path(__file__).resolve().parents[2];out=a.out.resolve()
if out.exists() or root in out.parents:raise RuntimeError('Use new scratch outside checkout')
shutil.copytree(root,out,ignore=shutil.ignore_patterns('.git','bin','obj','__pycache__'))
path=out/'NotaryGeek.PublicKnowledge.Worker/Services/PublicKnowledgeRunStorageService.cs';s=path.read_text();before=hashlib.sha256(path.read_bytes()).hexdigest()
if a.mutant=='archive':
    old='Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },';new='Conditions = null,'
    assert s.index(old)<s.index('// Compare and swap the latest pointer.')
    s=s.replace(old,new,1)
else:
    old='ValidateCaseExecution(previous, message, regressionCase);\n            return previous;'
    new='ValidateCaseExecution(previous, message, regressionCase);\n            return previous with { Phase = "admitted" };'
    assert s.count(old)==1;s=s.replace(old,new)
path.write_text(s)
(out/'mutation.json').write_text(json.dumps({'control':a.mutant,'path':str(path.relative_to(out)),'before':before,'after':hashlib.sha256(path.read_bytes()).hexdigest(),'old':old,'new':new},indent=2)+'\n')
print(out)
