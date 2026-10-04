"""Generate scratch-only project; reference the unchanged real worker project."""
import argparse,json,pathlib,subprocess,hashlib
p=argparse.ArgumentParser();p.add_argument('--out',type=pathlib.Path,required=True);a=p.parse_args()
here=pathlib.Path(__file__).resolve().parent;root=here.parents[1];out=a.out.resolve();out.mkdir(parents=True,exist_ok=False)
(out/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
(out/'W29Worker.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><EnableDefaultCompileItems>false</EnableDefaultCompileItems><UseSharedCompilation>false</UseSharedCompilation></PropertyGroup><ItemGroup><Compile Include="{here / 'W29Worker.cs.fixture'}"/><ProjectReference Include="{root / 'NotaryGeek.PublicKnowledge.Worker/NotaryGeek.PublicKnowledge.Worker.csproj'}"/></ItemGroup></Project>''')
print(out/'W29Worker.csproj')
