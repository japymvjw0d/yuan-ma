"""Offline preview: renders a synthetic Survivalcraft-like scene through the ShaderMod v2 pipeline (same GLSL)."""
import os, sys, math, time, gzip, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from gl3 import *
from enginemath import *
from assemble import program_sources
from PIL import Image

W, H = int(sys.argv[2]) if len(sys.argv) > 2 else 640, int(sys.argv[3]) if len(sys.argv) > 3 else 360
DOME_W, DOME_H = 2048, 512
SHADOW_RES, SHADOW_DIST, SHADOW_RANGE = 2048, 128.0, 64.0
DATA = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'ShaderMod', 'Data')
SETTINGS = dict(vanillaGain=1.0, lightingStrength=1.0, shadowLift=0.35, exposureBias=0.0, exposureSceneScale=1.0, bloomStrength=1.0)

create_context(16, 16)

# ---------------- resources
lut16 = np.frombuffer(gzip.open(os.path.join(DATA, 'AtmosphereLut.rgba16f.gz')).read(), '<f2').reshape(33, 128, 256, 4).copy()
atmosphereLut = texture3d(256, 128, 33, GL_RGBA16F, GL_RGBA, GL_HALF_FLOAT, lut16)
noise = np.frombuffer(gzip.open(os.path.join(DATA, 'Noise2D.rgba8.gz')).read(), np.uint8).reshape(256, 256, 4).copy()
noisetex = texture2d(256, 256, GL_RGBA8, GL_RGBA, GL_UNSIGNED_BYTE, noise, GL_LINEAR, GL_REPEAT)

def rt(w, h, fmt=GL_RGBA16F, typ=GL_HALF_FLOAT, filt=GL_LINEAR, wrap=GL_CLAMP_TO_EDGE):
    return texture2d(w, h, fmt, GL_RGBA, typ, None, filt, wrap)

skyMap = rt(256, 256); fbSky = fbo(skyMap)
cloudDome = rt(DOME_W, DOME_H, wrap=GL_REPEAT); fbDome = fbo(cloudDome)
shadowColor = rt(SHADOW_RES, SHADOW_RES, GL_RGBA8, GL_UNSIGNED_BYTE)
shadowDepth = texture2d(SHADOW_RES, SHADOW_RES, GL_DEPTH24_STENCIL8, GL_DEPTH_STENCIL, GL_UNSIGNED_INT_24_8, None, GL_NEAREST)
fbShadow = fbo(shadowColor, shadowDepth)
sceneColor = rt(W, H, GL_RGBA8, GL_UNSIGNED_BYTE, GL_NEAREST)
sceneDepth = texture2d(W, H, GL_DEPTH24_STENCIL8, GL_DEPTH_STENCIL, GL_UNSIGNED_INT_24_8, None, GL_NEAREST)
fbScene = fbo(sceneColor, sceneDepth)
waterColor = rt(W, H, GL_RGBA8, GL_UNSIGNED_BYTE, GL_NEAREST)
waterDepth = texture2d(W, H, GL_DEPTH24_STENCIL8, GL_DEPTH_STENCIL, GL_UNSIGNED_INT_24_8, None, GL_NEAREST)
fbWater = fbo(waterColor, waterDepth)
deferredTex = rt(W, H); fbDeferred = fbo(deferredTex)
fogTex = rt(W // 2, H // 2); fbFog = fbo(fogTex)
compositeTex = rt(W, H); fbComposite = fbo(compositeTex)
bloom = []; bw, bh = W, H
for i in range(7):
    bw, bh = max(1, bw // 2), max(1, bh // 2)
    a = rt(bw, bh); b = rt(bw, bh)
    bloom.append((bw, bh, a, fbo(a), b, fbo(b)))
finalTex = rt(W, H, GL_RGBA8, GL_UNSIGNED_BYTE); fbFinal = fbo(finalTex)
heightTex = texture2d(512, 512, GL_R8, GL_RED, GL_UNSIGNED_BYTE, np.zeros((512, 512), np.uint8), GL_NEAREST, GL_REPEAT)
sampRaw, sampCmp = sampler(False), sampler(True)

progs = {n: Program(*program_sources(n), name=n) for n in
         ['SkyCapture', 'CloudDome', 'SkyDraw', 'Deferred', 'VolumetricLight', 'Composite', 'BloomDown', 'BloomBlur', 'Grade']}

# ---------------- synthetic world (block cells: (x0,y0,z0,x1,y1,z1) inclusive-exclusive, colors)
GRASS = ((0.36, 0.62, 0.24), (0.55, 0.42, 0.28)); STONE = ((0.52, 0.52, 0.52),) * 2; WOOD = ((0.45, 0.33, 0.2),) * 2
LEAF = ((0.24, 0.5, 0.18),) * 2; SAND = ((0.85, 0.8, 0.6),) * 2; PLANK = ((0.7, 0.55, 0.35),) * 2
boxes = [
    ((-96, 60, -96, 96, 65, -8), GRASS), ((-96, 60, 8, 96, 65, 96), GRASS),
    ((-96, 60, -8, -10, 65, 8), GRASS), ((10, 60, -8, 96, 65, 8), GRASS),
    ((-10, 60, -8, 10, 62, 8), SAND),                         # pool floor (water surface at 64.9)
    ((-24, 65, -30, -18, 75, -24), STONE),                     # pillar
    ((16, 65, -20, 17, 71, -19), WOOD), ((13, 70, -23, 20, 74, -16), LEAF),   # tree
    ((-30, 65, 4, -29, 69, 5), PLANK), ((-20, 65, 4, -19, 69, 5), PLANK),
    ((-30, 65, 14, -29, 69, 15), PLANK), ((-20, 65, 14, -19, 69, 15), PLANK),
    ((-31, 69, 3, -18, 70, 16), PLANK),                        # roof (area below is "indoors")
    ((24, 65, 10, 30, 67, 16), STONE),
]
water = [(-10, 64.9, -8, 10, 64.9, 8)]

def box_tris(b, light=1.0):
    """Faces split into 1x1 block cells, like the game's per-block terrain geometry."""
    (x0, y0, z0, x1, y1, z1), (top, side) = b
    out = []
    def quad(c, col, ff):
        rgb = [ch * ff * light for ch in col]
        for i in (0, 1, 2, 0, 2, 3):
            out.extend(list(c[i]) + rgb + [1.0])
    def cells(a0, a1):
        a = a0
        while a < a1 - 1e-6:
            b = min(a + 1.0, a1); yield a, b; a = b
    for x, xb in cells(x0, x1):
        for z, zb in cells(z0, z1):
            quad([(x, y1, z), (x, y1, zb), (xb, y1, zb), (xb, y1, z)], top, 1.0)
    for y, yb in cells(y0, y1):
        for x, xb in cells(x0, x1):
            quad([(x, y, z1), (xb, y, z1), (xb, yb, z1), (x, yb, z1)], side, 0.84)
            quad([(xb, y, z0), (x, y, z0), (x, yb, z0), (xb, yb, z0)], side, 0.84)
        for z, zb in cells(z0, z1):
            quad([(x1, y, zb), (x1, y, z), (x1, yb, z), (x1, yb, zb)], side, 0.62)
            quad([(x0, y, z), (x0, y, zb), (x0, yb, zb), (x0, yb, z)], side, 0.62)
    if y0 > 61:
        for x, xb in cells(x0, x1):
            for z, zb in cells(z0, z1):
                quad([(x, y0, z), (xb, y0, z), (xb, y0, zb), (x, y0, zb)], side, 0.5)
    return out

def vbo(data):
    arr = np.array(data, np.float32)
    b = ctypes.c_uint(); gl.glGenBuffers(1, ctypes.byref(b)); gl.glBindBuffer(GL_ARRAY_BUFFER, b.value)
    gl.glBufferData(GL_ARRAY_BUFFER, arr.nbytes, arr.ctypes.data_as(ctypes.c_void_p), GL_STATIC_DRAW)
    v = ctypes.c_uint(); gl.glGenVertexArrays(1, ctypes.byref(v)); gl.glBindVertexArray(v.value)
    gl.glEnableVertexAttribArray(0); gl.glVertexAttribPointer(0, 3, GL_FLOAT, 0, 28, ctypes.c_void_p(0))
    gl.glEnableVertexAttribArray(1); gl.glVertexAttribPointer(1, 4, GL_FLOAT, 0, 28, ctypes.c_void_p(12))
    return v.value, len(arr) // 7

def lit_boxes(sky_light=1.0):
    data = []
    for b in boxes:
        (x0, y0, z0, x1, y1, z1), _ = b
        indoor = -31 <= x0 < -18 and 3 <= z0 < 16 and y1 <= 69
        data += box_tris(b, max(0.85, sky_light * 0.55) if indoor else sky_light)
    return data
scene_cache = {}
def scene_vao(sky_light):
    key = round(sky_light, 3)
    if key not in scene_cache: scene_cache[key] = vbo(lit_boxes(sky_light))
    return scene_cache[key]
sceneVao, sceneCount = scene_vao(1.0)
waterVao, waterCount = vbo(sum((box_tris(((x0, y0 - 0.1, z0, x1, y0, z1), ((0.2, 0.3, 0.8),) * 2)) for x0, y0, z0, x1, _, z1 in water), []))

# heightmap (top block cell index per column, x/z mod 256)
hm = np.zeros((512, 512), np.uint8)
for (x0, y0, z0, x1, y1, z1), _ in boxes:
    for x in range(x0, x1):
        for z in range(z0, z1):
            hm[z & 511, x & 511] = max(hm[z & 511, x & 511], y1 - 1)
gl.glBindTexture(GL_TEXTURE_2D, heightTex); gl.glPixelStorei(0x0CF5, 1)
gl.glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, 512, 512, GL_RED, GL_UNSIGNED_BYTE, hm.ctypes.data_as(ctypes.c_void_p))

VS_SCENE = """#version 300 es
layout(location = 0) in vec3 a_position; layout(location = 1) in vec4 a_color;
uniform mat4 u_viewProjectionMatrix; uniform vec3 u_origin3; out vec4 v_color;
void main() { v_color = a_color; gl_Position = u_viewProjectionMatrix * vec4(a_position - u_origin3, 1.0);
 gl_Position.y *= -1.0; gl_Position.z = 2.0 * gl_Position.z - gl_Position.w; }"""
FS_SCENE = """#version 300 es
precision highp float; in vec4 v_color; out vec4 o; void main() { o = v_color; }"""
VS_SHADOW = """#version 300 es
layout(location = 0) in vec3 a_position; uniform mat4 u_shadowMatrix; uniform vec3 u_origin3;
void main() { vec4 clip = u_shadowMatrix * vec4(a_position - u_origin3, 1.0);
 vec2 v = clip.xy * 1.165; vec2 v2 = v * v; float df = sqrt(sqrt(v2.x * v2.x + v2.y * v2.y)) * 0.9 + 0.1;
 gl_Position = vec4(clip.xy / df, clip.z * 0.2, 1.0); }"""
FS_WHITE = """#version 300 es
precision highp float; out vec4 o; void main() { o = vec4(1.0); }"""
pScene = Program(VS_SCENE, FS_SCENE, 'scene'); pShadow = Program(VS_SHADOW, FS_WHITE, 'shadow')

def render(time_of_day, cam_pos, cam_target, rain=0.0, out='preview.png', dump=False):
    t0 = time.time()
    sun = sun_direction(time_of_day, 0.5, 0.35)
    # vanilla Survivalcraft sky light (vertex colours): full by day, dim at night
    sky_light = 0.22 + 0.78 * min(max((sun[1] + 0.1) / 0.3, 0.0), 1.0)
    sceneVao, sceneCount = scene_vao(sky_light)
    light = sun if sun[1] >= 0 else -sun
    cam_pos = np.array(cam_pos, float)
    view = look_at(cam_pos, np.array(cam_target, float), np.array([0, 1.0, 0]))
    near, far = 0.1, 400.0
    proj = perspective(math.radians(70), W / H, near, far)
    view_rot = view.copy(); view_rot[3, :3] = 0
    smv, sproj = shadow_matrices(light, cam_pos, SHADOW_DIST, SHADOW_RANGE)
    me_fade = 0.37 + 1.2 * max(0.0, -sun[1]) if sun[1] < 0.18 else 1.7
    me_weight = min(max(1.0 - me_fade * abs(sun[1] - 0.18), 0.0), 1.0) ** 2
    wtc = 1234.5
    U = dict(
        gbufferModelView=view_rot, gbufferModelViewInverse=np.linalg.inv(view_rot), gbufferProjection=proj, gbufferProjectionInverse=np.linalg.inv(proj),
        shadowModelView=smv, shadowProjection=sproj, shadowProjectionInverse=np.linalg.inv(sproj))
    F = dict(cameraPosition=tuple(cam_pos), worldSunVector=tuple(sun), worldLightVector=tuple(light),
             eyeAltitude=cam_pos[1], wetness=rain, frameTimeCounter=12.3, worldTimeCounter=wtc, moonPhase=0.0, nightVision=0.0,
             isLightningFlashing=0.0, near=near, far=far, timeNoon=(1.0 if sun[1] > 0 else 0.0) * (1 - me_weight),
             timeMidnight=(1.0 if sun[1] < 0 else 0.0) * (1 - me_weight), timeSunrise=(1.0 if sun[0] > 0 else 0.0) * me_weight,
             timeSunset=(1.0 if sun[0] < 0 else 0.0) * me_weight, meWeight=me_weight, eyeSkylightFix=1.0,
             volFogDensity=1.0 + rain * 2.0, volFogWind=(wtc * 0.01, 0.0, wtc * 0.006), renderDistance=160.0,
             shadowDistance=SHADOW_DIST, shadowMapRes=float(SHADOW_RES), screenSize=(W, H), screenPixelSize=(1 / W, 1 / H), **SETTINGS)

    def setup(p):
        p.use()
        for k, v in U.items(): p.m4(k, v)
        for k, v in F.items():
            if isinstance(v, tuple): p.f(k, *v)
            else: p.f(k, v)
        p.i('isEyeInWater', 0); p.i('frameCounter', 0)
        p.tex('skyMap', skyMap); p.tex('atmosphereLut', atmosphereLut, GL_TEXTURE_3D); p.tex('noisetex', noisetex)

    def target(fb, w, h):
        gl.glBindFramebuffer(GL_FRAMEBUFFER, fb); gl.glViewport(0, 0, w, h)

    gl.glDisable(GL_DEPTH_TEST); gl.glDisable(GL_BLEND); gl.glDisable(GL_CULL_FACE)
    # 1 sky capture
    p = progs['SkyCapture']; setup(p); target(fbSky, 256, 256); fullscreen()
    # 2 cloud dome (full refresh in preview)
    p = progs['CloudDome']; setup(p); p.f('domeSize', DOME_W, DOME_H); target(fbDome, DOME_W, DOME_H); fullscreen()
    # 3 shadow map
    origin = np.array([math.floor(cam_pos[0]), 0.0, math.floor(cam_pos[2])])
    target(fbShadow, SHADOW_RES, SHADOW_RES); gl.glClearColor(0, 0, 0, 0); gl.glClearDepthf(ctypes.c_float(1.0))
    gl.glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT | GL_STENCIL_BUFFER_BIT)
    gl.glEnable(GL_DEPTH_TEST); gl.glDepthFunc(GL_LEQUAL)
    pShadow.use(); pShadow.m4('u_shadowMatrix', translation(origin - cam_pos) @ smv @ sproj); pShadow.f('u_origin3', *origin)
    gl.glBindVertexArray(sceneVao); gl.glDrawArrays(GL_TRIANGLES, 0, sceneCount)
    # 4 scene: sky first (like the game at draw order -100), then terrain
    target(fbScene, W, H); gl.glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT | GL_STENCIL_BUFFER_BIT)
    gl.glEnable(GL_DEPTH_TEST); gl.glDepthFunc(GL_LESS)
    vp = translation(origin - cam_pos) @ view_rot @ proj
    pScene.use(); pScene.m4('u_viewProjectionMatrix', vp); pScene.f('u_origin3', *origin)
    gl.glBindVertexArray(sceneVao); gl.glDrawArrays(GL_TRIANGLES, 0, sceneCount)
    gl.glDepthFunc(GL_LEQUAL); gl.glDepthMask(0)
    p = progs['SkyDraw']; setup(p); p.f('viewSize', W, H); p.tex('cloudDome', cloudDome); p.tex('heightMap', heightTex); fullscreen()
    gl.glDepthMask(1)
    # 5 water mask: copy scene depth, draw water
    gl.glBindFramebuffer(GL_READ_FRAMEBUFFER, fbScene); gl.glBindFramebuffer(GL_DRAW_FRAMEBUFFER, fbWater)
    gl.glBlitFramebuffer(0, 0, W, H, 0, 0, W, H, GL_DEPTH_BUFFER_BIT | GL_STENCIL_BUFFER_BIT, GL_NEAREST); check()
    target(fbWater, W, H); gl.glDepthFunc(GL_LEQUAL)
    pScene.use(); pScene.m4('u_viewProjectionMatrix', vp); pScene.f('u_origin3', *origin)
    gl.glBindVertexArray(waterVao); gl.glDrawArrays(GL_TRIANGLES, 0, waterCount)
    gl.glDisable(GL_DEPTH_TEST)
    # 6 deferred
    p = progs['Deferred']; setup(p)
    p.tex('sceneTex', sceneColor); p.tex('depthtex1', sceneDepth); p.tex('heightMap', heightTex); p.tex('cloudDome', cloudDome)
    p.tex('shadowtex0', shadowDepth, samp=sampRaw); p.tex('shadowtex1', shadowDepth, samp=sampCmp)
    target(fbDeferred, W, H); fullscreen()
    # 7 volumetric light
    p = progs['VolumetricLight']; setup(p); p.tex('depthtex0', waterDepth); p.tex('shadowtex1', shadowDepth, samp=sampCmp)
    target(fbFog, W // 2, H // 2); fullscreen()
    # 8 composite
    p = progs['Composite']; setup(p)
    p.tex('deferredTex', deferredTex); p.tex('fogTex', fogTex); p.tex('depthtex0', waterDepth); p.tex('depthtex1', sceneDepth)
    p.tex('heightMap', heightTex); p.tex('cloudDome', cloudDome)
    target(fbComposite, W, H); fullscreen()
    # 9 bloom
    src = compositeTex
    for i, (bw, bh, a, fa, b, fb) in enumerate(bloom):
        p = progs['BloomDown']; p.use(); p.tex('sourceTex', src); p.f('targetSize', bw, bh); p.f('firstLevel', 1.0 if i == 0 else 0.0)
        target(fa, bw, bh); fullscreen()
        p = progs['BloomBlur']; p.use(); p.tex('sourceTex', a); p.f('blurDirection', 1, 0); target(fb, bw, bh); fullscreen()
        p.use(); p.tex('sourceTex', b); p.f('blurDirection', 0, 1); target(fa, bw, bh); fullscreen()
        src = a
    # 10 grade
    p = progs['Grade']; setup(p)
    p.tex('compositeTex', compositeTex)
    for i, (_, _, a, _, _, _) in enumerate(bloom): p.tex(f'bloomTex{i + 1}', a)
    target(fbFinal, W, H); fullscreen()
    img = read_rgba8(W, H)
    Image.fromarray(img[..., :3]).save(out)  # texture row 0 = image top
    info = {}
    if dump:
        gl.glBindFramebuffer(GL_FRAMEBUFFER, fbSky); sky = read_float(256, 256)
        info['illum'] = sky[:4, 255, :3]
        gl.glBindFramebuffer(GL_FRAMEBUFFER, fbComposite); comp = read_float(W, H)
        info['nan'] = int(np.isnan(comp).sum()); info['comp_max'] = float(np.nanmax(comp[..., :3]))
        gl.glBindFramebuffer(GL_FRAMEBUFFER, fbDeferred); de = read_float(W, H)
        info['def_nan'] = int(np.isnan(de).sum())
        info['center'] = comp[H // 2, W // 2, :3]
    print(f'{out}: {time.time() - t0:.1f}s', info)
    return img

if __name__ == '__main__':
    tod = float(sys.argv[1]) if len(sys.argv) > 1 else 0.42
    render(tod, (6, 69, 34), (-2, 66, 0), out=f'preview_{tod:.2f}.png', dump=True)
