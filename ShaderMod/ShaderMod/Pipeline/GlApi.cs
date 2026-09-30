using System.Runtime.InteropServices;

namespace ShaderMod.Pipeline;

/// <summary>
/// 直接从驱动取得的 OpenGL ES 3.0 函数（不依赖游戏引擎所用的 GL 绑定库，1.9.2.1 与 1.9.3.1 都能用）。
/// Windows：原生模式用 wglGetProcAddress（opengl32.dll），ANGLE 兼容模式用 eglGetProcAddress（libEGL.dll）；
/// Linux：eglGetProcAddress（只用于离线测试）。必须在游戏的 GL 上下文所在线程（绘制线程）调用。
/// </summary>
internal static unsafe class GlApi
{
    public const uint GL_TEXTURE_2D = 0x0DE1, GL_TEXTURE_3D = 0x806F;
    public const uint GL_RGBA = 0x1908, GL_RED = 0x1903, GL_RGBA8 = 0x8058, GL_RGBA16F = 0x881A, GL_R8 = 0x8229;
    public const uint GL_UNSIGNED_BYTE = 0x1401, GL_FLOAT = 0x1406, GL_HALF_FLOAT = 0x140B;
    public const uint GL_DEPTH24_STENCIL8 = 0x88F0, GL_DEPTH_STENCIL = 0x84F9, GL_UNSIGNED_INT_24_8 = 0x84FA;
    public const uint GL_TEXTURE_MIN_FILTER = 0x2801, GL_TEXTURE_MAG_FILTER = 0x2800;
    public const uint GL_TEXTURE_WRAP_S = 0x2802, GL_TEXTURE_WRAP_T = 0x2803, GL_TEXTURE_WRAP_R = 0x8072;
    public const uint GL_NEAREST = 0x2600, GL_LINEAR = 0x2601, GL_REPEAT = 0x2901, GL_CLAMP_TO_EDGE = 0x812F;
    public const uint GL_TEXTURE_COMPARE_MODE = 0x884C, GL_TEXTURE_COMPARE_FUNC = 0x884D, GL_COMPARE_REF_TO_TEXTURE = 0x884E;
    public const uint GL_LEQUAL = 0x0203;
    public const uint GL_FRAMEBUFFER = 0x8D40, GL_READ_FRAMEBUFFER = 0x8CA8, GL_DRAW_FRAMEBUFFER = 0x8CA9;
    public const uint GL_COLOR_ATTACHMENT0 = 0x8CE0, GL_DEPTH_STENCIL_ATTACHMENT = 0x821A, GL_FRAMEBUFFER_COMPLETE = 0x8CD5;
    public const uint GL_TEXTURE0 = 0x84C0, GL_TRIANGLES = 0x0004;
    public const uint GL_DEPTH_TEST = 0x0B71, GL_BLEND = 0x0BE2, GL_CULL_FACE = 0x0B44, GL_SCISSOR_TEST = 0x0C11;
    public const uint GL_STENCIL_TEST = 0x0B90, GL_POLYGON_OFFSET_FILL = 0x8037, GL_DITHER = 0x0BD0;
    public const uint GL_COLOR_BUFFER_BIT = 0x4000, GL_DEPTH_BUFFER_BIT = 0x0100, GL_STENCIL_BUFFER_BIT = 0x0400;
    public const uint GL_COMPILE_STATUS = 0x8B81, GL_LINK_STATUS = 0x8B82, GL_INFO_LOG_LENGTH = 0x8B84;
    public const uint GL_VERTEX_SHADER = 0x8B31, GL_FRAGMENT_SHADER = 0x8B30;
    public const uint GL_UNPACK_ALIGNMENT = 0x0CF5, GL_PACK_ALIGNMENT = 0x0D05;
    public const uint GL_CURRENT_PROGRAM = 0x8B8D, GL_DRAW_FRAMEBUFFER_BINDING = 0x8CA6, GL_READ_FRAMEBUFFER_BINDING = 0x8CAA;
    public const uint GL_VIEWPORT = 0x0BA2, GL_SCISSOR_BOX = 0x0C10, GL_ACTIVE_TEXTURE = 0x84E0;
    public const uint GL_TEXTURE_BINDING_2D = 0x8069, GL_TEXTURE_BINDING_3D = 0x806A, GL_SAMPLER_BINDING = 0x8919;
    public const uint GL_VERTEX_ARRAY_BINDING = 0x85B5, GL_DEPTH_WRITEMASK = 0x0B72, GL_DEPTH_FUNC = 0x0B74, GL_COLOR_WRITEMASK = 0x0C23;
    public const uint GL_UNPACK_ROW_LENGTH = 0x0CF2, GL_PIXEL_UNPACK_BUFFER_BINDING = 0x88EF, GL_PIXEL_UNPACK_BUFFER = 0x88EC;
    public const uint GL_MAX_TEXTURE_SIZE = 0x0D33;
    public const uint GL_ALWAYS = 0x0207;

    public static delegate* unmanaged<uint> glGetError;
    public static delegate* unmanaged<uint, int*, void> glGetIntegerv;
    public static delegate* unmanaged<uint, byte*, void> glGetBooleanv;
    public static delegate* unmanaged<uint, byte> glIsEnabled;
    public static delegate* unmanaged<uint, void> glEnable;
    public static delegate* unmanaged<uint, void> glDisable;
    public static delegate* unmanaged<int, uint*, void> glGenTextures;
    public static delegate* unmanaged<int, uint*, void> glDeleteTextures;
    public static delegate* unmanaged<uint, uint, void> glBindTexture;
    public static delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void> glTexImage2D;
    public static delegate* unmanaged<uint, int, int, int, int, int, int, uint, uint, void*, void> glTexImage3D;
    public static delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void> glTexSubImage2D;
    public static delegate* unmanaged<uint, uint, int, void> glTexParameteri;
    public static delegate* unmanaged<uint, void> glActiveTexture;
    public static delegate* unmanaged<int, uint*, void> glGenSamplers;
    public static delegate* unmanaged<int, uint*, void> glDeleteSamplers;
    public static delegate* unmanaged<uint, uint, int, void> glSamplerParameteri;
    public static delegate* unmanaged<uint, uint, void> glBindSampler;
    public static delegate* unmanaged<int, uint*, void> glGenFramebuffers;
    public static delegate* unmanaged<int, uint*, void> glDeleteFramebuffers;
    public static delegate* unmanaged<uint, uint, void> glBindFramebuffer;
    public static delegate* unmanaged<uint, uint, uint, uint, int, void> glFramebufferTexture2D;
    public static delegate* unmanaged<uint, uint> glCheckFramebufferStatus;
    public static delegate* unmanaged<int, int, int, int, int, int, int, int, uint, uint, void> glBlitFramebuffer;
    public static delegate* unmanaged<uint, uint> glCreateShader;
    public static delegate* unmanaged<uint, int, byte**, int*, void> glShaderSource;
    public static delegate* unmanaged<uint, void> glCompileShader;
    public static delegate* unmanaged<uint, uint, int*, void> glGetShaderiv;
    public static delegate* unmanaged<uint, int, int*, byte*, void> glGetShaderInfoLog;
    public static delegate* unmanaged<uint, void> glDeleteShader;
    public static delegate* unmanaged<uint> glCreateProgram;
    public static delegate* unmanaged<uint, uint, void> glAttachShader;
    public static delegate* unmanaged<uint, void> glLinkProgram;
    public static delegate* unmanaged<uint, uint, int*, void> glGetProgramiv;
    public static delegate* unmanaged<uint, int, int*, byte*, void> glGetProgramInfoLog;
    public static delegate* unmanaged<uint, void> glDeleteProgram;
    public static delegate* unmanaged<uint, void> glUseProgram;
    public static delegate* unmanaged<uint, byte*, int> glGetUniformLocation;
    public static delegate* unmanaged<int, int, void> glUniform1i;
    public static delegate* unmanaged<int, float, void> glUniform1f;
    public static delegate* unmanaged<int, float, float, void> glUniform2f;
    public static delegate* unmanaged<int, float, float, float, void> glUniform3f;
    public static delegate* unmanaged<int, float, float, float, float, void> glUniform4f;
    public static delegate* unmanaged<int, int, byte, float*, void> glUniformMatrix4fv;
    public static delegate* unmanaged<int, uint*, void> glGenVertexArrays;
    public static delegate* unmanaged<int, uint*, void> glDeleteVertexArrays;
    public static delegate* unmanaged<uint, void> glBindVertexArray;
    public static delegate* unmanaged<uint, int, int, void> glDrawArrays;
    public static delegate* unmanaged<int, int, int, int, void> glViewport;
    public static delegate* unmanaged<int, int, int, int, void> glScissor;
    public static delegate* unmanaged<byte, byte, byte, byte, void> glColorMask;
    public static delegate* unmanaged<byte, void> glDepthMask;
    public static delegate* unmanaged<uint, void> glDepthFunc;
    public static delegate* unmanaged<uint, int, void> glPixelStorei;
    public static delegate* unmanaged<uint, uint, void> glBindBuffer;
    public static delegate* unmanaged<int, int, int, int, uint, uint, void*, void> glReadPixels;
    public static delegate* unmanaged<uint, void> glClear;
    public static delegate* unmanaged<float, float, float, float, void> glClearColor;
    public static delegate* unmanaged<float, void> glClearDepthf;

    public static bool IsLoaded { get; private set; }

    /// <summary>加载全部函数；任何一个取不到都会抛出异常（调用方负责回退到原版画面）</summary>
    public static void Load()
    {
        if (IsLoaded)
        {
            return;
        }
        Func<string, IntPtr> getProc = CreateLoader();
        IntPtr Get(string name)
        {
            IntPtr p = getProc(name);
            if (p == IntPtr.Zero || p == 1 || p == 2 || p == 3 || p == -1)
            {
                throw new EntryPointNotFoundException($"OpenGL function not found: {name}");
            }
            return p;
        }
        glGetError = (delegate* unmanaged<uint>)Get("glGetError");
        glGetIntegerv = (delegate* unmanaged<uint, int*, void>)Get("glGetIntegerv");
        glGetBooleanv = (delegate* unmanaged<uint, byte*, void>)Get("glGetBooleanv");
        glIsEnabled = (delegate* unmanaged<uint, byte>)Get("glIsEnabled");
        glEnable = (delegate* unmanaged<uint, void>)Get("glEnable");
        glDisable = (delegate* unmanaged<uint, void>)Get("glDisable");
        glGenTextures = (delegate* unmanaged<int, uint*, void>)Get("glGenTextures");
        glDeleteTextures = (delegate* unmanaged<int, uint*, void>)Get("glDeleteTextures");
        glBindTexture = (delegate* unmanaged<uint, uint, void>)Get("glBindTexture");
        glTexImage2D = (delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void>)Get("glTexImage2D");
        glTexImage3D = (delegate* unmanaged<uint, int, int, int, int, int, int, uint, uint, void*, void>)Get("glTexImage3D");
        glTexSubImage2D = (delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void>)Get("glTexSubImage2D");
        glTexParameteri = (delegate* unmanaged<uint, uint, int, void>)Get("glTexParameteri");
        glActiveTexture = (delegate* unmanaged<uint, void>)Get("glActiveTexture");
        glGenSamplers = (delegate* unmanaged<int, uint*, void>)Get("glGenSamplers");
        glDeleteSamplers = (delegate* unmanaged<int, uint*, void>)Get("glDeleteSamplers");
        glSamplerParameteri = (delegate* unmanaged<uint, uint, int, void>)Get("glSamplerParameteri");
        glBindSampler = (delegate* unmanaged<uint, uint, void>)Get("glBindSampler");
        glGenFramebuffers = (delegate* unmanaged<int, uint*, void>)Get("glGenFramebuffers");
        glDeleteFramebuffers = (delegate* unmanaged<int, uint*, void>)Get("glDeleteFramebuffers");
        glBindFramebuffer = (delegate* unmanaged<uint, uint, void>)Get("glBindFramebuffer");
        glFramebufferTexture2D = (delegate* unmanaged<uint, uint, uint, uint, int, void>)Get("glFramebufferTexture2D");
        glCheckFramebufferStatus = (delegate* unmanaged<uint, uint>)Get("glCheckFramebufferStatus");
        glBlitFramebuffer = (delegate* unmanaged<int, int, int, int, int, int, int, int, uint, uint, void>)Get("glBlitFramebuffer");
        glCreateShader = (delegate* unmanaged<uint, uint>)Get("glCreateShader");
        glShaderSource = (delegate* unmanaged<uint, int, byte**, int*, void>)Get("glShaderSource");
        glCompileShader = (delegate* unmanaged<uint, void>)Get("glCompileShader");
        glGetShaderiv = (delegate* unmanaged<uint, uint, int*, void>)Get("glGetShaderiv");
        glGetShaderInfoLog = (delegate* unmanaged<uint, int, int*, byte*, void>)Get("glGetShaderInfoLog");
        glDeleteShader = (delegate* unmanaged<uint, void>)Get("glDeleteShader");
        glCreateProgram = (delegate* unmanaged<uint>)Get("glCreateProgram");
        glAttachShader = (delegate* unmanaged<uint, uint, void>)Get("glAttachShader");
        glLinkProgram = (delegate* unmanaged<uint, void>)Get("glLinkProgram");
        glGetProgramiv = (delegate* unmanaged<uint, uint, int*, void>)Get("glGetProgramiv");
        glGetProgramInfoLog = (delegate* unmanaged<uint, int, int*, byte*, void>)Get("glGetProgramInfoLog");
        glDeleteProgram = (delegate* unmanaged<uint, void>)Get("glDeleteProgram");
        glUseProgram = (delegate* unmanaged<uint, void>)Get("glUseProgram");
        glGetUniformLocation = (delegate* unmanaged<uint, byte*, int>)Get("glGetUniformLocation");
        glUniform1i = (delegate* unmanaged<int, int, void>)Get("glUniform1i");
        glUniform1f = (delegate* unmanaged<int, float, void>)Get("glUniform1f");
        glUniform2f = (delegate* unmanaged<int, float, float, void>)Get("glUniform2f");
        glUniform3f = (delegate* unmanaged<int, float, float, float, void>)Get("glUniform3f");
        glUniform4f = (delegate* unmanaged<int, float, float, float, float, void>)Get("glUniform4f");
        glUniformMatrix4fv = (delegate* unmanaged<int, int, byte, float*, void>)Get("glUniformMatrix4fv");
        glGenVertexArrays = (delegate* unmanaged<int, uint*, void>)Get("glGenVertexArrays");
        glDeleteVertexArrays = (delegate* unmanaged<int, uint*, void>)Get("glDeleteVertexArrays");
        glBindVertexArray = (delegate* unmanaged<uint, void>)Get("glBindVertexArray");
        glDrawArrays = (delegate* unmanaged<uint, int, int, void>)Get("glDrawArrays");
        glViewport = (delegate* unmanaged<int, int, int, int, void>)Get("glViewport");
        glScissor = (delegate* unmanaged<int, int, int, int, void>)Get("glScissor");
        glColorMask = (delegate* unmanaged<byte, byte, byte, byte, void>)Get("glColorMask");
        glDepthMask = (delegate* unmanaged<byte, void>)Get("glDepthMask");
        glDepthFunc = (delegate* unmanaged<uint, void>)Get("glDepthFunc");
        glPixelStorei = (delegate* unmanaged<uint, int, void>)Get("glPixelStorei");
        glBindBuffer = (delegate* unmanaged<uint, uint, void>)Get("glBindBuffer");
        glReadPixels = (delegate* unmanaged<int, int, int, int, uint, uint, void*, void>)Get("glReadPixels");
        glClear = (delegate* unmanaged<uint, void>)Get("glClear");
        glClearColor = (delegate* unmanaged<float, float, float, float, void>)Get("glClearColor");
        glClearDepthf = (delegate* unmanaged<float, void>)Get("glClearDepthf");
        IsLoaded = true;
    }

    public static int GetInteger(uint pname)
    {
        int value = 0;
        glGetIntegerv(pname, &value);
        return value;
    }

    /// <summary>清空错误标志（进入本模组的绘制前调用，避免把引擎留下的错误算到自己头上）</summary>
    public static void ClearErrors()
    {
        for (int i = 0; i < 16 && glGetError() != 0; i++)
        {
        }
    }

    public static void CheckError(string where)
    {
        uint error = glGetError();
        if (error != 0)
        {
            ClearErrors();
            throw new InvalidOperationException($"OpenGL error 0x{error:X} in {where}");
        }
    }

    // ---------------- 函数地址来源

    static Func<string, IntPtr> CreateLoader()
    {
        if (OperatingSystem.IsWindows())
        {
            // ANGLE 兼容模式：游戏已加载 libEGL.dll 且有当前 EGL 上下文。
            // 只查询已加载的模块，不主动加载（缺少配套 libGLESv2 时加载 libEGL 会直接终止进程）。
            IntPtr egl = GetModuleHandleW("libEGL.dll");
            if (egl != IntPtr.Zero
                && NativeLibrary.TryGetExport(egl, "eglGetCurrentContext", out IntPtr getCurrentContext)
                && ((delegate* unmanaged<IntPtr>)getCurrentContext)() != IntPtr.Zero
                && NativeLibrary.TryGetExport(egl, "eglGetProcAddress", out IntPtr eglGetProcAddress))
            {
                return name => CallGetProc(eglGetProcAddress, name);
            }
            // 原生模式：WGL（GL 1.1 的函数要从 opengl32.dll 的导出表取）
            IntPtr opengl32 = NativeLibrary.Load("opengl32.dll");
            IntPtr wglGetProcAddress = NativeLibrary.GetExport(opengl32, "wglGetProcAddress");
            return name =>
            {
                IntPtr p = CallGetProc(wglGetProcAddress, name);
                if (p == IntPtr.Zero || p == 1 || p == 2 || p == 3 || p == -1)
                {
                    NativeLibrary.TryGetExport(opengl32, name, out p);
                }
                return p;
            };
        }
        IntPtr libEgl = NativeLibrary.Load("libEGL.so.1");
        IntPtr getProcAddress = NativeLibrary.GetExport(libEgl, "eglGetProcAddress");
        return name => CallGetProc(getProcAddress, name);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandleW(string moduleName);

    static IntPtr CallGetProc(IntPtr getProc, string name)
    {
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(name + "\0");
        fixed (byte* p = bytes)
        {
            return ((delegate* unmanaged<byte*, IntPtr>)getProc)(p);
        }
    }
}
