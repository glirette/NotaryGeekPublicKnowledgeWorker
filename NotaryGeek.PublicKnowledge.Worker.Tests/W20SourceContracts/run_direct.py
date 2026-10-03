#!/usr/bin/env python3
"""Package-free, socket-denied direct methods. Not the repository build or VSTest.
Creates only disposable output outside the checkout; production .cs inputs are copied byte-for-byte.
"""
import argparse
import ctypes
import errno
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys

HERE = Path(__file__).resolve().parent


def fence():
    libc = ctypes.CDLL(None, use_errno=True)
    if libc.prctl(38, 1, 0, 0, 0):
        raise RuntimeError('no_new_privs unavailable; refusing execution')
    sec = ctypes.CDLL('libseccomp.so.2')
    sec.seccomp_init.restype = ctypes.c_void_p
    sec.seccomp_rule_add.argtypes = [ctypes.c_void_p, ctypes.c_uint32, ctypes.c_int, ctypes.c_uint]
    sec.seccomp_load.argtypes = [ctypes.c_void_p]
    sec.seccomp_release.argtypes = [ctypes.c_void_p]
    context = sec.seccomp_init(0x7fff0000)
    if not context:
        raise RuntimeError('seccomp allocation failed')
    try:
        for name in ['socket', 'socketpair', 'connect', 'sendto', 'sendmsg', 'sendmmsg', 'io_uring_setup']:
            number = sec.seccomp_syscall_resolve_name(name.encode())
            if number >= 0 and sec.seccomp_rule_add(context, 0x50000 | errno.EPERM, number, 0):
                raise RuntimeError('seccomp rule failed: ' + name)
        if sec.seccomp_load(context):
            raise RuntimeError('seccomp load failed; refusing execution')
    finally:
        sec.seccomp_release(context)


def probe():
    import socket
    for family in (socket.AF_INET, socket.AF_INET6, socket.AF_UNIX):
        try:
            connection = socket.socket(family)
        except OSError as error:
            if error.errno != errno.EPERM:
                raise
        else:
            connection.close()
            raise RuntimeError('network-denial probe failed')


def bounded(command, environment, cwd, log):
    with log.open('w') as output:
        process = subprocess.Popen(command, env=environment, cwd=cwd, stdout=output,
                                   stderr=subprocess.STDOUT, close_fds=True, start_new_session=True)
        try:
            return process.wait(timeout=120)
        finally:
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            process.wait()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', type=Path, required=True, help='Existing trusted .NET 10 SDK executable')
    parser.add_argument('--out', type=Path, required=True, help='New output directory outside the checkout')
    parser.add_argument('--mutant', choices=['per-hop', 'auto-redirect', 'wrong-name', 'collapsed-identity', 'original-selection', 'original-location'])
    args = parser.parse_args()
    root = HERE.parents[1]
    out = args.out.resolve()
    sdk = args.dotnet.resolve()
    if out == root or root in out.parents or out.exists():
        raise RuntimeError('Output must be new and outside the checkout')
    os.environ.clear()
    # Fence this parent before spawning any child. Every descendant inherits the filter.
    fence()
    probe()
    out.mkdir(parents=True)
    for directory in ['cli', 'packages', 'tmp', 'inputs']:
        (out / directory).mkdir()
    env = {'PATH': '/usr/bin:/bin', 'DOTNET_ROOT': str(sdk.parent),
           'DOTNET_CLI_HOME': str(out / 'cli'), 'NUGET_PACKAGES': str(out / 'packages'),
           'DOTNET_CLI_TELEMETRY_OPTOUT': '1', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE': '1',
           'DOTNET_NOLOGO': '1', 'DOTNET_CLI_USE_MSBUILD_SERVER': '0',
           'MSBUILDDISABLENODEREUSE': '1', 'DOTNET_EnableDiagnostics': '0', 'TMPDIR': str(out / 'tmp')}
    rc = bounded([sys.executable, str(Path(__file__).resolve()), '--probe-child'], env, out, out / 'isolation.log')
    if rc:
        raise RuntimeError('Child isolation probe failed')
    print('Parent and exec-child AF_INET/AF_INET6/AF_UNIX denial verified', flush=True)
    worker = root / 'NotaryGeek.PublicKnowledge.Worker'
    inputs = list((worker / 'Configuration').glob('*.cs')) + list((worker / 'Models').glob('*.cs'))
    inputs += [worker / 'Services' / (name + '.cs') for name in (
        'AllowedSourceRedirects', 'SourceHttpClientRegistration', 'PublicKnowledgeResearchService',
        'PublicKnowledgeSourceIndexService', 'PublicKnowledgeProviderOutput', 'PublicKnowledgeExecutionPolicy')]
    inputs += [root / 'NotaryGeek.PublicKnowledge.Worker.Tests/SourceRedirectTests.cs'] + sorted(HERE.glob('*.cs'))
    hashes = {str(path.relative_to(root)): hashlib.sha256(path.read_bytes()).hexdigest()
              for path in [Path(__file__).resolve(), HERE / 'assertion-shim.cs.txt', HERE / 'direct-main.cs.txt']}
    hashes['sdk-executable'] = hashlib.sha256(sdk.read_bytes()).hexdigest()
    for path in sorted((worker / 'public-knowledge').rglob('*.json')):
        hashes[str(path.relative_to(root))] = hashlib.sha256(path.read_bytes()).hexdigest()
    copied = {}
    for source in inputs:
        data = source.read_bytes()
        relative = str(source.relative_to(root))
        hashes[relative] = hashlib.sha256(data).hexdigest()
        target = out / 'inputs' / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
        copied[source.name] = target
    if args.mutant:
        variants = {
            'per-hop': ('AllowedSourceRedirects.cs', [('TryValidate(current.AbsoluteUri, allowedSourceHosts', 'TryValidate(originalUri.AbsoluteUri, allowedSourceHosts'), ('TryValidate(target.AbsoluteUri, allowedSourceHosts', 'TryValidate(originalUri.AbsoluteUri, allowedSourceHosts')]),
            'auto-redirect': ('SourceHttpClientRegistration.cs', [('AllowAutoRedirect = false', 'AllowAutoRedirect = true')]),
            'wrong-name': ('PublicKnowledgeResearchService.cs', [('_httpClientFactory.CreateClient(nameof(PublicKnowledgeResearchService))', '_httpClientFactory.CreateClient("OpenAI")')]),
            'collapsed-identity': ('PublicKnowledgeProviderOutput.cs', [('return builder.Uri.AbsoluteUri;', 'return builder.Uri.AbsoluteUri.ToLowerInvariant();')]),
            'original-selection': ('PublicKnowledgeResearchService.cs', [('command.RequestedUrls.SequenceEqual(first, StringComparer.Ordinal)', 'command.RequestedUrls.SequenceEqual(first, StringComparer.OrdinalIgnoreCase)'), ('.DistinctBy(SourceFetchIdentity, StringComparer.Ordinal)', '.Distinct(StringComparer.OrdinalIgnoreCase)')]),
            'original-location': ('AllowedSourceRedirects.cs', [('response.Headers.NonValidated.TryGetValues', 'response.Headers.TryGetValues')])}
        name, edits = variants[args.mutant]
        target = copied[name]
        text = target.read_text()
        for old, new in edits:
            if old not in text:
                raise RuntimeError('Mutation target missing: ' + old)
            text = text.replace(old, new)
        target.write_text(text)
        hashes['mutated-copy:' + name] = hashlib.sha256(target.read_bytes()).hexdigest()
    # Only the immutable record declaration is needed to compile the unchanged execution policy.
    # Storage implementation, Functions host, queues, credentials and provider execution are excluded.
    storage = (worker / 'Services/PublicKnowledgeRunStorageService.cs').read_text()
    declaration = storage[storage.index('public sealed record PublicKnowledgeStoredRunEnvelope('):
                          storage.index('public sealed record PublicKnowledgeStoredRunReceipt(')]
    (out / 'Envelope.cs').write_text('using NotaryGeek.PublicKnowledge.Worker.Models;\nnamespace NotaryGeek.PublicKnowledge.Worker.Services;\n' + declaration)
    hashes['extracted:PublicKnowledgeStoredRunEnvelope'] = hashlib.sha256(declaration.encode()).hexdigest()
    shutil.copyfile(HERE / 'assertion-shim.cs.txt', out / 'Shim.cs')
    shutil.copyfile(HERE / 'direct-main.cs.txt', out / 'Run.cs')
    # Existing baseline methods read the public bundled law fixture; these copies are never fetched.
    shutil.copytree(worker / 'public-knowledge', out / 'public-knowledge')
    (out / 'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
    (out / 'direct.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><UseSharedCompilation>false</UseSharedCompilation></PropertyGroup>
<ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /><Using Include="Xunit" />
<Content Include="public-knowledge/**/*.json" CopyToOutputDirectory="PreserveNewest" /></ItemGroup>
</Project>''')
    (out / 'source-bindings.json').write_text(json.dumps(hashes, indent=2, sort_keys=True) + '\n')
    rc = bounded([str(sdk), 'build', 'direct.csproj', '--configfile', 'NuGet.Config', '-m:1', '--nologo'], env, out, out / 'build.log')
    if rc:
        print((out / 'build.log').read_text())
        return rc
    rc = bounded([str(sdk), str(out / 'bin/Debug/net10.0/direct.dll')], env, out, out / 'run.log')
    print((out / 'run.log').read_text())
    print('Direct-method exit:', rc, '; mutant:', args.mutant or 'none', '; NOT VSTest')
    return rc


if __name__ == '__main__':
    if sys.argv[1:] == ['--probe-child']:
        probe()
        print('Exec child: AF_INET/AF_INET6/AF_UNIX denied by inherited filter')
    else:
        sys.exit(main())
