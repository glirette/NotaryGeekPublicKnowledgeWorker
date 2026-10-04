"""Generate scratch-only project; reference the unchanged real worker project."""
import argparse,json,pathlib,subprocess,hashlib
p=argparse.ArgumentParser();p.add_argument('--out',type=pathlib.Path,required=True);p.add_argument('--tests',action='store_true',help='Build the direct-method test runner instead of the process worker');a=p.parse_args()
here=pathlib.Path(__file__).resolve().parent;root=here.parents[1];out=a.out.resolve();out.mkdir(parents=True,exist_ok=False)
(out/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
name='DirectTests' if a.tests else 'W29Worker'
reference=root / ('NotaryGeek.PublicKnowledge.Worker.Tests/NotaryGeek.PublicKnowledge.Worker.Tests.csproj' if a.tests else 'NotaryGeek.PublicKnowledge.Worker/NotaryGeek.PublicKnowledge.Worker.csproj')
(out/(name+'.csproj')).write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><EnableDefaultCompileItems>false</EnableDefaultCompileItems><UseSharedCompilation>false</UseSharedCompilation></PropertyGroup><ItemGroup><Compile Include="{here / (name+'.cs.fixture')}"/><ProjectReference Include="{reference}"/></ItemGroup></Project>''')
print(out/(name+'.csproj'))
