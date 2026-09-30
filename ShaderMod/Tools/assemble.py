"""Python mirror of ShaderSource.cs: expands #include (once per file) and prepends version/defines/prelude."""
import os, re
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'ShaderMod', 'Glsl')
INC = re.compile(r'^\s*#include\s+"([^"]+)"\s*$')

PROGRAMS = {
    # name: (vs defines, fs file, fs defines)
    'SkyCapture':      ([], 'program/SkyCapture.fsh', []),
    'CloudDome':       (['NEED_ILLUMINANCE'], 'program/CloudDome.fsh', []),
    'SkyDraw':         (['NEED_ILLUMINANCE', 'FAR_PLANE'], 'program/SkyDraw.fsh', []),
    'Deferred':        (['NEED_ILLUMINANCE', 'NEED_SKY_SH'], 'program/Deferred.fsh', ['CLOUDS_SHADOW']),
    'VolumetricLight': (['NEED_ILLUMINANCE'], 'program/VolumetricLight.fsh', []),
    'Composite':       (['NEED_ILLUMINANCE'], 'program/Composite.fsh', []),
    'BloomDown':       ([], 'program/BloomDown.fsh', []),
    'BloomBlur':       ([], 'program/BloomBlur.fsh', []),
    'Grade':           (['NEED_ILLUMINANCE'], 'program/Grade.fsh', []),
}

def read(path):
    with open(os.path.join(ROOT, path), encoding='utf-8-sig') as f:
        return f.read()

def expand(path, seen, out):
    if path in seen:
        return
    seen.add(path)
    base = os.path.dirname(path)
    for line in read(path).splitlines():
        m = INC.match(line)
        if m:
            target = m.group(1)
            target = target.lstrip('/') if target.startswith('/') else os.path.normpath(os.path.join(base, target)).replace('\\', '/')
            expand(target, seen, out)
        else:
            out.append(line)

def assemble(path, defines):
    out = ['#version 300 es']
    out += [f'#define {d}' for d in defines]
    seen = set()
    expand('Prelude.glsl', seen, out)
    expand(path, seen, out)
    return '\n'.join(out) + '\n'

def program_sources(name):
    vsd, fs, fsd = PROGRAMS[name]
    return assemble('program/FullScreen.vsh', vsd), assemble(fs, fsd)
