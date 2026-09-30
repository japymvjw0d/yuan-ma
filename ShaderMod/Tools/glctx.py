"""Headless OpenGL ES 3 context (Mesa llvmpipe via EGL surfaceless) + raw ctypes GLES functions."""
import os, ctypes
os.environ.setdefault("PYOPENGL_PLATFORM", "egl")
os.environ.setdefault("EGL_PLATFORM", "surfaceless")
from OpenGL.EGL import *

def create_context(width=64, height=64):
    dpy = eglGetDisplay(EGL_DEFAULT_DISPLAY)
    major, minor = EGLint(), EGLint()
    eglInitialize(dpy, ctypes.pointer(major), ctypes.pointer(minor))
    attribs = (EGLint * 13)(EGL_SURFACE_TYPE, EGL_PBUFFER_BIT, EGL_RED_SIZE, 8, EGL_GREEN_SIZE, 8, EGL_BLUE_SIZE, 8,
                            EGL_ALPHA_SIZE, 8, EGL_RENDERABLE_TYPE, 0x40, EGL_NONE)
    cfg = EGLConfig(); n = EGLint()
    eglChooseConfig(dpy, attribs, ctypes.pointer(cfg), 1, ctypes.pointer(n))
    surf = eglCreatePbufferSurface(dpy, cfg, (EGLint * 5)(EGL_WIDTH, width, EGL_HEIGHT, height, EGL_NONE))
    eglBindAPI(EGL_OPENGL_ES_API)
    ctx = eglCreateContext(dpy, cfg, EGL_NO_CONTEXT, (EGLint * 3)(EGL_CONTEXT_CLIENT_VERSION, 3, EGL_NONE))
    assert eglMakeCurrent(dpy, surf, surf, ctx)
    return dpy

gl = ctypes.CDLL("libGLESv2.so.2")
gl.glGetString.restype = ctypes.c_char_p
gl.glCreateShader.restype = ctypes.c_uint
gl.glCreateProgram.restype = ctypes.c_uint
gl.glGetUniformLocation.restype = ctypes.c_int
gl.glGetAttribLocation.restype = ctypes.c_int
GL_VERTEX_SHADER, GL_FRAGMENT_SHADER = 0x8B31, 0x8B30
GL_COMPILE_STATUS, GL_LINK_STATUS, GL_INFO_LOG_LENGTH = 0x8B81, 0x8B82, 0x8B84
GL_ACTIVE_UNIFORMS, GL_ACTIVE_ATTRIBUTES = 0x8B86, 0x8B89

def compile_shader(kind, src):
    sh = gl.glCreateShader(kind)
    b = src.encode("utf-8")
    arr = (ctypes.c_char_p * 1)(b)
    gl.glShaderSource(sh, 1, arr, None)
    gl.glCompileShader(sh)
    ok = ctypes.c_int(); gl.glGetShaderiv(sh, GL_COMPILE_STATUS, ctypes.byref(ok))
    log = ctypes.create_string_buffer(8192); gl.glGetShaderInfoLog(sh, 8192, None, log)
    return sh, ok.value == 1, log.value.decode(errors="replace")

def link(vs_src, ps_src):
    vs, okv, logv = compile_shader(GL_VERTEX_SHADER, vs_src)
    ps, okp, logp = compile_shader(GL_FRAGMENT_SHADER, ps_src)
    if not (okv and okp):
        return None, f"compile failed\nVS: {logv}\nPS: {logp}"
    prog = gl.glCreateProgram()
    gl.glAttachShader(prog, vs); gl.glAttachShader(prog, ps); gl.glLinkProgram(prog)
    ok = ctypes.c_int(); gl.glGetProgramiv(prog, GL_LINK_STATUS, ctypes.byref(ok))
    log = ctypes.create_string_buffer(8192); gl.glGetProgramInfoLog(prog, 8192, None, log)
    if ok.value != 1:
        return None, "link failed: " + log.value.decode(errors="replace")
    return prog, (logv + logp).strip()

def active(prog, which):
    count = ctypes.c_int()
    gl.glGetProgramiv(prog, GL_ACTIVE_UNIFORMS if which == "u" else GL_ACTIVE_ATTRIBUTES, ctypes.byref(count))
    names = []
    for i in range(count.value):
        name = ctypes.create_string_buffer(256); ln = ctypes.c_int(); size = ctypes.c_int(); typ = ctypes.c_uint()
        fn = gl.glGetActiveUniform if which == "u" else gl.glGetActiveAttrib
        fn(prog, i, 256, ctypes.byref(ln), ctypes.byref(size), ctypes.byref(typ), name)
        names.append((name.value.decode().split("[")[0], typ.value))
    return names
