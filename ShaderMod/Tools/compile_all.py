import sys, subprocess, tempfile, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from assemble import PROGRAMS, program_sources
from glctx import create_context, link, active

def glslang(src, stage):
    with tempfile.NamedTemporaryFile('w', suffix='.' + stage, delete=False) as f:
        f.write(src); p = f.name
    r = subprocess.run(['glslangValidator', p], capture_output=True, text=True)
    os.unlink(p)
    return r.returncode == 0, r.stdout.strip()

def numbered(src, around=None):
    lines = src.splitlines()
    return '\n'.join(f'{i+1:5d}: {l}' for i, l in enumerate(lines) if around is None or abs(i + 1 - around) < 6)

create_context()
ok_all = True
names = sys.argv[1:] or list(PROGRAMS)
for name in names:
    vs, fs = program_sources(name)
    for stage, src in (('vert', vs), ('frag', fs)):
        ok, log = glslang(src, stage)
        if not ok:
            ok_all = False
            print(f'== {name} {stage}: glslang FAILED\n{log[:3000]}')
            import re
            m = re.search(r'ERROR: 0:(\d+):', log)
            if m: print(numbered(src, int(m.group(1))))
    prog, log = link(vs, fs)
    if prog is None:
        ok_all = False
        print(f'== {name}: Mesa FAILED\n{log[:3000]}')
    else:
        us = sorted(n for n, _ in active(prog, 'u'))
        print(f'== {name}: OK ({len(us)} uniforms) {log[:500]}')
print('ALL OK' if ok_all else 'FAILURES')
