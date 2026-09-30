"""Minimal GLES 3 helpers on top of glctx (ctypes)."""
import ctypes, numpy as np, sys
from glctx import gl, create_context, compile_shader, GL_VERTEX_SHADER, GL_FRAGMENT_SHADER

GL_TEXTURE_2D, GL_TEXTURE_3D = 0x0DE1, 0x806F
GL_RGBA, GL_RGBA8, GL_RGBA16F, GL_R8, GL_RED = 0x1908, 0x8058, 0x881A, 0x8229, 0x1903
GL_UNSIGNED_BYTE, GL_FLOAT, GL_HALF_FLOAT = 0x1401, 0x1406, 0x140B
GL_DEPTH24_STENCIL8, GL_DEPTH_STENCIL, GL_UNSIGNED_INT_24_8 = 0x88F0, 0x84F9, 0x84FA
GL_DEPTH_COMPONENT24, GL_DEPTH_COMPONENT, GL_UNSIGNED_INT = 0x81A6, 0x1902, 0x1405
GL_TEXTURE_MIN_FILTER, GL_TEXTURE_MAG_FILTER = 0x2801, 0x2800
GL_TEXTURE_WRAP_S, GL_TEXTURE_WRAP_T, GL_TEXTURE_WRAP_R = 0x2802, 0x2803, 0x8072
GL_NEAREST, GL_LINEAR, GL_REPEAT, GL_CLAMP_TO_EDGE = 0x2600, 0x2601, 0x2901, 0x812F
GL_TEXTURE_COMPARE_MODE, GL_TEXTURE_COMPARE_FUNC, GL_COMPARE_REF_TO_TEXTURE, GL_LEQUAL = 0x884C, 0x884D, 0x884E, 0x0203
GL_FRAMEBUFFER, GL_READ_FRAMEBUFFER, GL_DRAW_FRAMEBUFFER = 0x8D40, 0x8CA8, 0x8CA9
GL_COLOR_ATTACHMENT0, GL_DEPTH_ATTACHMENT, GL_DEPTH_STENCIL_ATTACHMENT = 0x8CE0, 0x8D00, 0x821A
GL_FRAMEBUFFER_COMPLETE = 0x8CD5
GL_TEXTURE0 = 0x84C0
GL_TRIANGLES = 0x0004
GL_DEPTH_TEST, GL_BLEND, GL_CULL_FACE, GL_SCISSOR_TEST = 0x0B71, 0x0BE2, 0x0B44, 0x0C11
GL_COLOR_BUFFER_BIT, GL_DEPTH_BUFFER_BIT, GL_STENCIL_BUFFER_BIT = 0x4000, 0x0100, 0x0400
GL_ARRAY_BUFFER, GL_STATIC_DRAW = 0x8892, 0x88E4
GL_LESS = 0x0201

gl.glCheckFramebufferStatus.restype = ctypes.c_uint

def check():
    e = gl.glGetError()
    if e: raise RuntimeError(f'GL error 0x{e:x}')

def texture2d(w, h, internal, fmt, typ, data=None, filt=GL_LINEAR, wrap=GL_CLAMP_TO_EDGE):
    t = ctypes.c_uint(); gl.glGenTextures(1, ctypes.byref(t)); t = t.value
    gl.glBindTexture(GL_TEXTURE_2D, t)
    ptr = None if data is None else data.ctypes.data_as(ctypes.c_void_p)
    gl.glTexImage2D(GL_TEXTURE_2D, 0, internal, w, h, 0, fmt, typ, ptr)
    for p, v in ((GL_TEXTURE_MIN_FILTER, filt), (GL_TEXTURE_MAG_FILTER, filt), (GL_TEXTURE_WRAP_S, wrap), (GL_TEXTURE_WRAP_T, wrap)):
        gl.glTexParameteri(GL_TEXTURE_2D, p, v)
    check()
    return t

def texture3d(w, h, d, internal, fmt, typ, data):
    t = ctypes.c_uint(); gl.glGenTextures(1, ctypes.byref(t)); t = t.value
    gl.glBindTexture(GL_TEXTURE_3D, t)
    gl.glTexImage3D(GL_TEXTURE_3D, 0, internal, w, h, d, 0, fmt, typ, data.ctypes.data_as(ctypes.c_void_p))
    for p, v in ((GL_TEXTURE_MIN_FILTER, GL_LINEAR), (GL_TEXTURE_MAG_FILTER, GL_LINEAR), (GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE),
                 (GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE), (GL_TEXTURE_WRAP_R, GL_CLAMP_TO_EDGE)):
        gl.glTexParameteri(GL_TEXTURE_3D, p, v)
    check()
    return t

def sampler(compare=False):
    s = ctypes.c_uint(); gl.glGenSamplers(1, ctypes.byref(s)); s = s.value
    filt = GL_LINEAR if compare else GL_NEAREST
    gl.glSamplerParameteri(s, GL_TEXTURE_MIN_FILTER, filt); gl.glSamplerParameteri(s, GL_TEXTURE_MAG_FILTER, filt)
    gl.glSamplerParameteri(s, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE); gl.glSamplerParameteri(s, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE)
    if compare:
        gl.glSamplerParameteri(s, GL_TEXTURE_COMPARE_MODE, GL_COMPARE_REF_TO_TEXTURE)
        gl.glSamplerParameteri(s, GL_TEXTURE_COMPARE_FUNC, GL_LEQUAL)
    return s

def fbo(color=None, depth=None, depth_attachment=GL_DEPTH_STENCIL_ATTACHMENT):
    f = ctypes.c_uint(); gl.glGenFramebuffers(1, ctypes.byref(f)); f = f.value
    gl.glBindFramebuffer(GL_FRAMEBUFFER, f)
    if color: gl.glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, color, 0)
    if depth: gl.glFramebufferTexture2D(GL_FRAMEBUFFER, depth_attachment, GL_TEXTURE_2D, depth, 0)
    st = gl.glCheckFramebufferStatus(GL_FRAMEBUFFER)
    if st != GL_FRAMEBUFFER_COMPLETE: raise RuntimeError(f'FBO incomplete 0x{st:x}')
    return f

class Program:
    def __init__(self, vs, fs, name=''):
        v, okv, lv = compile_shader(GL_VERTEX_SHADER, vs)
        f, okf, lf = compile_shader(GL_FRAGMENT_SHADER, fs)
        if not (okv and okf): raise RuntimeError(f'{name} compile: {lv} {lf}')
        self.p = gl.glCreateProgram(); gl.glAttachShader(self.p, v); gl.glAttachShader(self.p, f)
        gl.glBindAttribLocation(self.p, 0, b'a_position'); gl.glBindAttribLocation(self.p, 1, b'a_color')
        gl.glLinkProgram(self.p)
        ok = ctypes.c_int(); gl.glGetProgramiv(self.p, 0x8B82, ctypes.byref(ok))
        if ok.value != 1:
            log = ctypes.create_string_buffer(4096); gl.glGetProgramInfoLog(self.p, 4096, None, log)
            raise RuntimeError(f'{name} link: {log.value}')
        self.name = name; self.unit = 0
    def use(self):
        gl.glUseProgram(self.p); self.unit = 0
    def loc(self, n): return gl.glGetUniformLocation(self.p, n.encode())
    def f(self, n, *v):
        l = self.loc(n)
        if l < 0: return
        fn = [None, gl.glUniform1f, gl.glUniform2f, gl.glUniform3f, gl.glUniform4f][len(v)]
        fn(l, *[ctypes.c_float(x) for x in v])
    def i(self, n, v):
        l = self.loc(n)
        if l >= 0: gl.glUniform1i(l, v)
    def m4(self, n, m):
        l = self.loc(n)
        if l >= 0:
            a = np.ascontiguousarray(m, dtype=np.float32)
            gl.glUniformMatrix4fv(l, 1, 0, a.ctypes.data_as(ctypes.c_void_p))
    def tex(self, n, t, target=GL_TEXTURE_2D, samp=0):
        l = self.loc(n)
        if l < 0: return
        gl.glActiveTexture(GL_TEXTURE0 + self.unit); gl.glBindTexture(target, t); gl.glBindSampler(self.unit, samp)
        gl.glUniform1i(l, self.unit); self.unit += 1

_vao = None
def fullscreen():
    global _vao
    if _vao is None:
        v = ctypes.c_uint(); gl.glGenVertexArrays(1, ctypes.byref(v)); _vao = v.value
    gl.glBindVertexArray(_vao); gl.glDrawArrays(GL_TRIANGLES, 0, 3); check()

def read_rgba8(w, h):
    buf = np.zeros((h, w, 4), np.uint8)
    gl.glReadPixels(0, 0, w, h, GL_RGBA, GL_UNSIGNED_BYTE, buf.ctypes.data_as(ctypes.c_void_p)); check()
    return buf

def read_float(w, h):
    buf = np.zeros((h, w, 4), np.float32)
    gl.glReadPixels(0, 0, w, h, GL_RGBA, GL_FLOAT, buf.ctypes.data_as(ctypes.c_void_p)); check()
    return buf
