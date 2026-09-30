import os, re, subprocess, sys, tempfile, ctypes, random
import glctx
from glctx import gl, link, active

MOD = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "ShaderMod")
VAN = os.environ.get("SC_VANILLA_SHADERS", "")  # 原版 Survivalcraft/Content/Assets/Shaders 目录
failures = 0
def check(ok, what):
    global failures
    print(("PASS " if ok else "FAIL ") + what)
    if not ok: failures += 1

def engine_source(code, vertex, macros):
    """Mirror Engine.Graphics.Shader.PrependShaderMacros for a shader without a #version line."""
    s = "#version 100\n#define GLSL\n"
    if vertex:
        s += "#define OPENGL_POSITION_FIX gl_Position.y *= u_glymul; gl_Position.z = 2.0 * gl_Position.z - gl_Position.w;\n"
        s += "uniform float u_glymul;\n"
    for name in macros:
        s += f"#define {name} \n"
    return s + "#line 1\n" + code

def metadata(*codes):
    sem, samp = {}, {}
    for c in codes:
        for m in re.finditer(r"//\s*<Semantic Name='(\w+)' Attribute='(\w+)'\s*/>", c): sem[m.group(2)] = m.group(1)
        for m in re.finditer(r"//\s*<Sampler Name='(\w+)' Texture='(\w+)'\s*/>", c): samp[m.group(2)] = m.group(1)
    return sem, samp

def glslang(src, stage):
    with tempfile.NamedTemporaryFile("w", suffix="." + stage, delete=False) as f:
        f.write(src); path = f.name
    r = subprocess.run(["glslangValidator", path], capture_output=True, text=True)
    os.unlink(path)
    return r.returncode == 0, (r.stdout + r.stderr).strip()

GL_SAMPLER_2D = 0x8B5E
def build(name, vs, ps, macros):
    vsrc, psrc = engine_source(vs, True, macros), engine_source(ps, False, macros)
    okv, logv = glslang(vsrc, "vert"); okp, logp = glslang(psrc, "frag")
    check(okv and okp, f"{name}: glslangValidator (GLSL ES 1.00)" + ("" if okv and okp else f"\n{logv}\n{logp}"))
    prog, log = link(vsrc, psrc)
    check(prog is not None, f"{name}: Mesa GLES 3.2 compile + link" + ("" if prog else f"\n{log}"))
    if prog is None: return None, set()
    sem, samp = metadata(vs, ps)
    attrs = active(prog, "a"); unis = active(prog, "u")
    missing_sem = [a for a, _ in attrs if a not in sem]
    check(not missing_sem, f"{name}: every active attribute has a Semantic {missing_sem or ''}")
    missing_samp = [u for u, t in unis if t == GL_SAMPLER_2D and u not in samp]
    check(not missing_samp, f"{name}: every texture has Sampler metadata {missing_samp or ''}")
    return prog, {u for u, _ in unis}

def read(path): return open(path, encoding="utf-8").read()

# ---------------- terrain shaders ----------------
terrain = {"Opaque": ["Opaque"], "AlphaTested": ["ALPHATESTED"], "Transparent": ["Transparent"]}
required = {"Opaque": ["u_fogBottomTopDensity", "u_hazeStartDensity"],
            "AlphaTested": ["u_fogBottomTopDensity", "u_hazeStartDensity", "u_alphaThreshold"],
            "Transparent": ["u_fogBottomTopDensity", "u_hazeStartDensity"]}
ours_expected = {"Opaque": ["u_vanillaFogOff"], "AlphaTested": ["u_vanillaFogOff"],
                 "Transparent": ["u_vanillaFogOff", "u_hideWater", "u_waterSlots"]}
glctx.create_context(64, 64)
programs = {}
for name, macros in terrain.items():
    prog, unis = build(f"{name} (mod)", read(f"{MOD}/Assets/Shaders/{name}.vsh"), read(f"{MOD}/Assets/Shaders/{name}.psh"), macros)
    vprog, vunis = build(f"{name} (vanilla)", read(f"{VAN}/{name}.vsh"), read(f"{VAN}/{name}.psh"), macros)
    programs[name] = (prog, vprog)
    lost = sorted(vunis - unis)
    check(not lost, f"{name}: all vanilla uniforms still active in mod shader {lost or ''}")
    req = [u for u in required[name] if u not in unis]
    check(not req, f"{name}: uniforms TerrainRenderer reads non-null are active {req or ''}")
    ours = [u for u in ours_expected[name] if u not in unis]
    check(not ours, f"{name}: ShaderMod uniforms active {ours or ''}")

# ---------------- engine-compiled pass shaders (shadow map, water depth) ----------------
ENG = f"{MOD}/EngineShaders"
engine_expected = {"ShadowTerrain": ["u_texture", "u_shadowMatrix", "u_origin"],
                   "WaterMask": ["u_texture", "u_viewProjectionMatrix", "u_origin", "u_waterSlots"]}
for name, exp in engine_expected.items():
    prog, unis = build(f"Engine/{name}", read(f"{ENG}/{name}.vsh"), read(f"{ENG}/{name}.psh"), [])
    miss = [u for u in exp if u not in unis]
    check(not miss, f"Engine/{name}: uniforms set by C# / DrawTerrainChunkGeometrySubsets are active {miss or ''}")

# ---------------- render tests ----------------
W = H = 64
def loc(p, n): return gl.glGetUniformLocation(p, n.encode())
def u1(p, n, v): gl.glUniform1f(loc(p, n), ctypes.c_float(v))
def u2(p, n, a, b): gl.glUniform2f(loc(p, n), ctypes.c_float(a), ctypes.c_float(b))
def u3(p, n, a, b, c): gl.glUniform3f(loc(p, n), ctypes.c_float(a), ctypes.c_float(b), ctypes.c_float(c))
def u1i(p, n, v): gl.glUniform1i(loc(p, n), v)
def mat(p, n, m):
    arr = (ctypes.c_float * 16)(*m); gl.glUniformMatrix4fv(loc(p, n), 1, 0, arr)

def make_texture(w, h, pixels, linear=True):
    tex = ctypes.c_uint(); gl.glGenTextures(1, ctypes.byref(tex)); gl.glBindTexture(0x0DE1, tex.value)
    buf = (ctypes.c_ubyte * len(pixels))(*pixels)
    gl.glTexImage2D(0x0DE1, 0, 0x1908, w, h, 0, 0x1908, 0x1401, buf)
    f = 0x2601 if linear else 0x2600
    for pname, v in ((0x2801, f), (0x2800, f), (0x2802, 0x812F), (0x2803, 0x812F)): gl.glTexParameteri(0x0DE1, pname, v)
    return tex.value

def read_pixels(w, h):
    buf = (ctypes.c_ubyte * (w * h * 4))(); gl.glReadPixels(0, 0, w, h, 0x1908, 0x1401, buf); return bytes(buf)

def bind_attr(prog, name, data, comps, normalized=False, ctype=ctypes.c_float, gltype=0x1406):
    l = gl.glGetAttribLocation(prog, name.encode())
    if l < 0: return None
    arr = (ctype * len(data))(*data)
    gl.glEnableVertexAttribArray(l); gl.glVertexAttribPointer(l, comps, gltype, 1 if normalized else 0, 0, arr)
    return arr

def draw_terrain(prog, strength, name):
    gl.glViewport(0, 0, W, H); gl.glClearColor(0, 0, 0, 1); gl.glClear(0x4000 | 0x100)
    gl.glUseProgram(prog)
    # a slanted quad in front of the camera, in world coordinates near (1000, 64, 1000) to exercise precision
    ox, oz = 1000.0, 1000.0
    pos = [ox-2, 62, oz+5,  ox+2, 62, oz+5,  ox+2, 66, oz+7,   ox-2, 62, oz+5,  ox+2, 66, oz+7,  ox-2, 66, oz+7]
    col = []
    for i in range(6): col += [200, 180, 150, 0 if name == "Transparent" else 255]
    uv = [0,0, 1,0, 1,1, 0,0, 1,1, 0,1]
    keep = [bind_attr(prog, "a_position", pos, 3),
            bind_attr(prog, "a_color", col, 4, True, ctypes.c_ubyte, 0x1401),
            bind_attr(prog, "a_texcoord", uv, 2)]
    # simple perspective looking down +z from (ox, 64, oz) — column-major, glsl mat * vec
    f, n_, fa = 1.0 / 0.7, 0.1, 100.0
    proj = [f,0,0,0, 0,f,0,0, 0,0,(fa+n_)/(n_-fa),-1, 0,0,2*fa*n_/(n_-fa),0]
    # view: camera at (0,64,0) relative to origin, looking toward +z → flip z
    view = [1,0,0,0, 0,1,0,0, 0,0,-1,0, 0,-64,0,1]
    def mul(a, b):  # column-major 4x4: a*b
        return [sum(a[k*4+r] * b[c*4+k] for k in range(4)) for c in range(4) for r in range(4)]
    mat(prog, "u_viewProjectionMatrix", mul(proj, view))
    u2(prog, "u_origin", ox, oz); u3(prog, "u_viewPosition", ox, 64, oz); u1(prog, "u_glymul", 1.0)
    u1(prog, "u_fogYMultiplier", 1.0); u3(prog, "u_fogColor", 0.6, 0.7, 0.9)
    u3(prog, "u_fogBottomTopDensity", 0.0, 128.0, 0.01); u2(prog, "u_hazeStartDensity", 3.0, 0.2)
    if name == "AlphaTested": u1(prog, "u_alphaThreshold", 0.5)
    u1(prog, "u_shaderStrength", strength); u3(prog, "u_sunDirection", 0.3, 0.8, -0.52); u1(prog, "u_daylight", 1.0)
    u1(prog, "u_rain", 0.0); u1(prog, "u_time", 12.5)
    gl.glActiveTexture(0x84C0); gl.glBindTexture(0x0DE1, TEX); u1i(prog, "u_texture", 0)
    gl.glDrawArrays(4, 0, 6)
    return read_pixels(W, H)


def u4(p, n, a, b, c, d): gl.glUniform4f(loc(p, n), ctypes.c_float(a), ctypes.c_float(b), ctypes.c_float(c), ctypes.c_float(d))

def draw_terrain2(prog, name, fog_off=0.0, hide_water=0.0, slots=(-1, -1, -1, -1), uv_cell=(0, 0)):
    gl.glViewport(0, 0, W, H); gl.glClearColor(ctypes.c_float(0), ctypes.c_float(0), ctypes.c_float(0), ctypes.c_float(1)); gl.glClear(0x4000 | 0x100)
    gl.glUseProgram(prog)
    ox, oz = 1000.0, 1000.0
    pos = [ox-2, 62, oz+5,  ox+2, 62, oz+5,  ox+2, 66, oz+7,   ox-2, 62, oz+5,  ox+2, 66, oz+7,  ox-2, 66, oz+7]
    col = []
    for i in range(6): col += [200, 180, 150, 0 if name == "Transparent" else 255]
    cu, cv = uv_cell
    uv = [(cu + a * 0.9 + 0.05) / 16.0 if i % 2 == 0 else (cv + a * 0.9 + 0.05) / 16.0
          for i, a in enumerate([0,0, 1,0, 1,1, 0,0, 1,1, 0,1])]
    keep = [bind_attr(prog, "a_position", pos, 3),
            bind_attr(prog, "a_color", col, 4, True, ctypes.c_ubyte, 0x1401),
            bind_attr(prog, "a_texcoord", uv, 2)]
    f, n_, fa = 1.0 / 0.7, 0.1, 100.0
    proj = [f,0,0,0, 0,f,0,0, 0,0,(fa+n_)/(n_-fa),-1, 0,0,2*fa*n_/(n_-fa),0]
    view = [1,0,0,0, 0,1,0,0, 0,0,-1,0, 0,-64,0,1]
    def mul(a, b):
        return [sum(a[k*4+r] * b[c*4+k] for k in range(4)) for c in range(4) for r in range(4)]
    mat(prog, "u_viewProjectionMatrix", mul(proj, view))
    u2(prog, "u_origin", ox, oz); u3(prog, "u_viewPosition", ox, 64, oz); u1(prog, "u_glymul", 1.0)
    u1(prog, "u_fogYMultiplier", 1.0); u3(prog, "u_fogColor", 0.6, 0.7, 0.9)
    u3(prog, "u_fogBottomTopDensity", 0.0, 128.0, 0.01); u2(prog, "u_hazeStartDensity", 3.0, 0.2)
    if name == "AlphaTested": u1(prog, "u_alphaThreshold", 0.5)
    u1(prog, "u_vanillaFogOff", fog_off); u1(prog, "u_hideWater", hide_water); u4(prog, "u_waterSlots", *slots)
    gl.glActiveTexture(0x84C0); gl.glBindTexture(0x0DE1, TEX); u1i(prog, "u_texture", 0)
    gl.glDrawArrays(4, 0, 6)
    return read_pixels(W, H)

def covered(px): return sum(1 for i in range(0, len(px), 4) if px[i:i+3] != b"\x00\x00\x00")

random.seed(1)
tex_pixels = []
for i in range(256 * 256): tex_pixels += [random.randint(40, 255), random.randint(40, 255), random.randint(40, 255), 255]
TEX = make_texture(256, 256, tex_pixels, linear=False)
for name in terrain:
    prog, vprog = programs[name]
    if not prog or not vprog: continue
    draw_terrain2(vprog, name)
    vanilla = draw_terrain2(vprog, name)
    off = draw_terrain2(prog, name)
    maxdiff = max(abs(a - b) for a, b in zip(vanilla, off))
    check(covered(vanilla) > 200 and maxdiff <= 1, f"{name}: parameters unset renders identically to vanilla ({covered(vanilla)} px, max diff {maxdiff})")
    nofog = draw_terrain2(prog, name, fog_off=1.0)
    check(nofog != off, f"{name}: u_vanillaFogOff = 1 removes the vanilla fog")
    if name == "Transparent":
        hidden = draw_terrain2(prog, name, hide_water=1.0, slots=(3 + 5 * 16, -1, -1, -1), uv_cell=(3, 5))
        check(covered(hidden) == 0, f"Transparent: water (slot 83) is discarded when u_hideWater = 1 ({covered(hidden)} px left)")
        kept = draw_terrain2(prog, name, hide_water=1.0, slots=(3 + 5 * 16, -1, -1, -1), uv_cell=(4, 5))
        check(covered(kept) > 200, f"Transparent: other translucent blocks (slot 84) are still drawn ({covered(kept)} px)")

print("ALL PASSED" if failures == 0 else f"{failures} FAILED")
sys.exit(1 if failures else 0)
