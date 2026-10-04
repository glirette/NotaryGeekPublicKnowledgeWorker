"""Inherited socket denial and credential-free environment for all W29 execution."""
import ctypes, errno, os, pathlib, signal, socket, subprocess, sys

def fence():
    libc=ctypes.CDLL(None, use_errno=True)
    if libc.prctl(38,1,0,0,0): raise RuntimeError('no_new_privs failed')
    sec=ctypes.CDLL('libseccomp.so.2');sec.seccomp_init.restype=ctypes.c_void_p
    sec.seccomp_rule_add.argtypes=[ctypes.c_void_p,ctypes.c_uint32,ctypes.c_int,ctypes.c_uint]
    sec.seccomp_load.argtypes=[ctypes.c_void_p];sec.seccomp_release.argtypes=[ctypes.c_void_p]
    ctx=sec.seccomp_init(0x7fff0000)
    if not ctx: raise RuntimeError('seccomp allocation failed')
    try:
        for name in ('socket','socketpair','connect','sendto','sendmsg','sendmmsg','io_uring_setup'):
            n=sec.seccomp_syscall_resolve_name(name.encode())
            if n>=0 and sec.seccomp_rule_add(ctx,0x50000|errno.EPERM,n,0): raise RuntimeError(name)
        if sec.seccomp_load(ctx): raise RuntimeError('seccomp load failed')
    finally: sec.seccomp_release(ctx)

def probe():
    for family in (socket.AF_INET,socket.AF_INET6,socket.AF_UNIX):
        try: s=socket.socket(family)
        except OSError as e:
            if e.errno!=errno.EPERM: raise
        else:
            s.close();raise RuntimeError('socket denial failed')

def main():
    sdk,cache,home, *command=sys.argv[1:]
    home=pathlib.Path(home).resolve();home.mkdir(parents=True,exist_ok=True)
    env={'PATH':'/usr/bin:/bin','DOTNET_ROOT':str(pathlib.Path(sdk).resolve().parent),
         'DOTNET_CLI_HOME':str(home),'NUGET_PACKAGES':str(pathlib.Path(cache).resolve()),
         'DOTNET_NOLOGO':'1','DOTNET_GENERATE_ASPNET_CERTIFICATE':'false',
         'DOTNET_CLI_TELEMETRY_OPTOUT':'1','DOTNET_SKIP_FIRST_TIME_EXPERIENCE':'1',
         'DOTNET_EnableDiagnostics':'0','DOTNET_CLI_USE_MSBUILD_SERVER':'0','MSBUILDDISABLENODEREUSE':'1'}
    os.environ.clear();os.environ.update(env);
    # Become subreaper so hard-deadline cleanup can reap descendants as well.
    if ctypes.CDLL(None).prctl(36,1,0,0,0): raise RuntimeError('subreaper unavailable')
    fence();probe()
    subprocess.run([sys.executable,'-c','from isolate import probe;probe()',],cwd=pathlib.Path(__file__).parent,env=env,check=True,timeout=10)
    p=subprocess.Popen(command,env=env,start_new_session=True,close_fds=True)
    try: return p.wait(timeout=900)
    finally:
        # W08 workers intentionally own process groups. Walk descendants before killing the parent.
        owned={p.pid};changed=True
        while changed:
            changed=False
            for item in pathlib.Path('/proc').iterdir():
                if not item.name.isdigit():continue
                try:parent=int((item/'stat').read_text().split(') ',1)[1].split()[1])
                except (OSError,ValueError,IndexError):continue
                if parent in owned and int(item.name) not in owned:owned.add(int(item.name));changed=True
        for pid in sorted(owned,reverse=True):
            try:os.kill(pid,signal.SIGKILL)
            except ProcessLookupError:pass
        p.wait()
        for pid in owned-{p.pid}:
            try:os.waitpid(pid,0)
            except ChildProcessError:pass
if __name__=='__main__':sys.exit(main())
