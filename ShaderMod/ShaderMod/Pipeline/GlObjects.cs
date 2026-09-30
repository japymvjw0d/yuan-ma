using System.Text;
using static ShaderMod.Pipeline.GlApi;

namespace ShaderMod.Pipeline;

/// <summary>原生 GL 纹理（2D / 3D / 深度）</summary>
internal sealed unsafe class GlTexture : IDisposable
{
    public uint Handle { get; private set; }
    public uint Target { get; }
    public int Width { get; }
    public int Height { get; }

    GlTexture(uint target, int width, int height)
    {
        Target = target;
        Width = width;
        Height = height;
        uint handle;
        glGenTextures(1, &handle);
        Handle = handle;
        glBindTexture(target, handle);
    }

    /// <summary>颜色纹理；data 为 null 时只分配</summary>
    public static GlTexture Create2D(int width, int height, uint internalFormat, uint format, uint type, bool linear, bool repeat, void* data = null)
    {
        GlTexture t = new(GL_TEXTURE_2D, width, height);
        glTexImage2D(GL_TEXTURE_2D, 0, (int)internalFormat, width, height, 0, format, type, data);
        t.SetSampling(linear, repeat);
        CheckError("Create2D");
        return t;
    }

    public static GlTexture CreateHdr(int width, int height, bool repeatX = false)
    {
        GlTexture t = Create2D(width, height, GL_RGBA16F, GL_RGBA, GL_HALF_FLOAT, true, false);
        if (repeatX)
        {
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, (int)GL_REPEAT);
        }
        return t;
    }

    /// <summary>DEPTH24_STENCIL8 深度纹理（可挂到帧缓冲，也可在着色器里采样）</summary>
    public static GlTexture CreateDepth(int width, int height)
    {
        GlTexture t = new(GL_TEXTURE_2D, width, height);
        glTexImage2D(GL_TEXTURE_2D, 0, (int)GL_DEPTH24_STENCIL8, width, height, 0, GL_DEPTH_STENCIL, GL_UNSIGNED_INT_24_8, null);
        t.SetSampling(false, false);
        CheckError("CreateDepth");
        return t;
    }

    public static GlTexture Create3D(int width, int height, int depth, uint internalFormat, uint format, uint type, void* data)
    {
        GlTexture t = new(GL_TEXTURE_3D, width, height);
        glTexImage3D(GL_TEXTURE_3D, 0, (int)internalFormat, width, height, depth, 0, format, type, data);
        glTexParameteri(GL_TEXTURE_3D, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR);
        glTexParameteri(GL_TEXTURE_3D, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
        glTexParameteri(GL_TEXTURE_3D, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
        glTexParameteri(GL_TEXTURE_3D, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
        glTexParameteri(GL_TEXTURE_3D, GL_TEXTURE_WRAP_R, (int)GL_CLAMP_TO_EDGE);
        CheckError("Create3D");
        return t;
    }

    void SetSampling(bool linear, bool repeat)
    {
        int filter = (int)(linear ? GL_LINEAR : GL_NEAREST);
        int wrap = (int)(repeat ? GL_REPEAT : GL_CLAMP_TO_EDGE);
        glTexParameteri(Target, GL_TEXTURE_MIN_FILTER, filter);
        glTexParameteri(Target, GL_TEXTURE_MAG_FILTER, filter);
        glTexParameteri(Target, GL_TEXTURE_WRAP_S, wrap);
        glTexParameteri(Target, GL_TEXTURE_WRAP_T, wrap);
    }

    public void Upload2D(int x, int y, int width, int height, uint format, uint type, void* data)
    {
        glBindTexture(GL_TEXTURE_2D, Handle);
        glTexSubImage2D(GL_TEXTURE_2D, 0, x, y, width, height, format, type, data);
    }

    public void Dispose()
    {
        if (Handle != 0)
        {
            uint handle = Handle;
            glDeleteTextures(1, &handle);
            Handle = 0;
        }
    }
}

/// <summary>原生 GL 帧缓冲；也可以包装引擎渲染目标已有的帧缓冲（不拥有，不删除）</summary>
internal sealed unsafe class GlFramebuffer : IDisposable
{
    readonly bool m_owned;

    public uint Handle { get; private set; }
    public int Width { get; }
    public int Height { get; }

    GlFramebuffer(uint handle, int width, int height, bool owned)
    {
        Handle = handle;
        Width = width;
        Height = height;
        m_owned = owned;
    }

    public static GlFramebuffer Create(GlTexture color, GlTexture depth = null)
    {
        uint handle;
        glGenFramebuffers(1, &handle);
        glBindFramebuffer(GL_FRAMEBUFFER, handle);
        if (color != null)
        {
            glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, color.Handle, 0);
        }
        if (depth != null)
        {
            glFramebufferTexture2D(GL_FRAMEBUFFER, GL_DEPTH_STENCIL_ATTACHMENT, GL_TEXTURE_2D, depth.Handle, 0);
        }
        GlTexture size = color ?? depth;
        GlFramebuffer fb = new(handle, size.Width, size.Height, true);
        fb.CheckComplete("Create");
        return fb;
    }

    /// <summary>包装引擎渲染目标的帧缓冲</summary>
    public static GlFramebuffer Wrap(int handle, int width, int height) => new((uint)handle, width, height, false);

    /// <summary>把深度纹理挂到这个帧缓冲（替换引擎渲染目标原来的深度缓冲）</summary>
    public void AttachDepth(GlTexture depth)
    {
        glBindFramebuffer(GL_FRAMEBUFFER, Handle);
        glFramebufferTexture2D(GL_FRAMEBUFFER, GL_DEPTH_STENCIL_ATTACHMENT, GL_TEXTURE_2D, depth.Handle, 0);
        CheckComplete("AttachDepth");
    }

    public void CheckComplete(string where)
    {
        uint status = glCheckFramebufferStatus(GL_FRAMEBUFFER);
        if (status != GL_FRAMEBUFFER_COMPLETE)
        {
            throw new InvalidOperationException($"Framebuffer incomplete (0x{status:X}) in {where}");
        }
    }

    public void Bind()
    {
        glBindFramebuffer(GL_FRAMEBUFFER, Handle);
        glViewport(0, 0, Width, Height);
    }

    public void Dispose()
    {
        if (m_owned && Handle != 0)
        {
            uint handle = Handle;
            glDeleteFramebuffers(1, &handle);
        }
        Handle = 0;
    }
}

/// <summary>原生 GL 着色器程序（GLSL ES 3.00），按名字设置 uniform 和纹理</summary>
internal sealed unsafe class GlProgram : IDisposable
{
    readonly Dictionary<string, int> m_locations = [];
    int m_nextUnit;

    public string Name { get; }
    public uint Handle { get; private set; }

    public GlProgram(string name, string vertexSource, string fragmentSource)
    {
        Name = name;
        uint vs = Compile(GL_VERTEX_SHADER, vertexSource, name + ".vert");
        uint fs;
        try
        {
            fs = Compile(GL_FRAGMENT_SHADER, fragmentSource, name + ".frag");
        }
        catch
        {
            glDeleteShader(vs);
            throw;
        }
        Handle = glCreateProgram();
        glAttachShader(Handle, vs);
        glAttachShader(Handle, fs);
        glLinkProgram(Handle);
        glDeleteShader(vs);
        glDeleteShader(fs);
        int ok;
        glGetProgramiv(Handle, GL_LINK_STATUS, &ok);
        if (ok == 0)
        {
            string log = ReadLog(Handle, false);
            Dispose();
            throw new InvalidOperationException($"Linking {name} failed: {log}");
        }
    }

    static uint Compile(uint kind, string source, string name)
    {
        uint shader = glCreateShader(kind);
        byte[] bytes = Encoding.UTF8.GetBytes(source);
        fixed (byte* p = bytes)
        {
            byte* ptr = p;
            int length = bytes.Length;
            glShaderSource(shader, 1, &ptr, &length);
        }
        glCompileShader(shader);
        int ok;
        glGetShaderiv(shader, GL_COMPILE_STATUS, &ok);
        if (ok == 0)
        {
            string log = ReadLog(shader, true);
            glDeleteShader(shader);
            throw new InvalidOperationException($"Compiling {name} failed: {log}");
        }
        return shader;
    }

    static string ReadLog(uint handle, bool shader)
    {
        int length;
        if (shader)
        {
            glGetShaderiv(handle, GL_INFO_LOG_LENGTH, &length);
        }
        else
        {
            glGetProgramiv(handle, GL_INFO_LOG_LENGTH, &length);
        }
        if (length <= 0)
        {
            return string.Empty;
        }
        byte[] buffer = new byte[length];
        fixed (byte* p = buffer)
        {
            if (shader)
            {
                glGetShaderInfoLog(handle, length, null, p);
            }
            else
            {
                glGetProgramInfoLog(handle, length, null, p);
            }
        }
        return Encoding.UTF8.GetString(buffer).TrimEnd('\0');
    }

    public void Use()
    {
        glUseProgram(Handle);
        m_nextUnit = 0;
    }

    int Location(string name)
    {
        if (!m_locations.TryGetValue(name, out int location))
        {
            byte[] bytes = Encoding.ASCII.GetBytes(name + "\0");
            fixed (byte* p = bytes)
            {
                location = glGetUniformLocation(Handle, p);
            }
            m_locations[name] = location;
        }
        return location;
    }

    public void Set(string name, float x)
    {
        int l = Location(name);
        if (l >= 0) glUniform1f(l, x);
    }

    public void Set(string name, float x, float y)
    {
        int l = Location(name);
        if (l >= 0) glUniform2f(l, x, y);
    }

    public void Set(string name, float x, float y, float z)
    {
        int l = Location(name);
        if (l >= 0) glUniform3f(l, x, y, z);
    }

    public void Set(string name, float x, float y, float z, float w)
    {
        int l = Location(name);
        if (l >= 0) glUniform4f(l, x, y, z, w);
    }

    public void SetInt(string name, int x)
    {
        int l = Location(name);
        if (l >= 0) glUniform1i(l, x);
    }

    /// <summary>引擎矩阵（行向量约定）按内存顺序上传：着色器里 m * v 即等于引擎的 v * M</summary>
    public void Set(string name, in Engine.Matrix m)
    {
        int l = Location(name);
        if (l < 0) return;
        float* f = stackalloc float[16]
        {
            m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44,
        };
        glUniformMatrix4fv(l, 1, 0, f);
    }

    /// <summary>把纹理绑到下一个纹理单元（Use 后从 0 开始），sampler 为 0 时用纹理自身的采样参数</summary>
    public void Texture(string name, GlTexture texture, uint sampler = 0) => Texture(name, texture.Target, texture.Handle, sampler);

    public void Texture(string name, uint target, uint handle, uint sampler = 0)
    {
        int l = Location(name);
        if (l < 0) return;
        if (m_nextUnit >= GlState.SavedTextureUnits)
        {
            throw new InvalidOperationException($"Too many textures in {Name}");
        }
        uint unit = (uint)m_nextUnit++;
        glActiveTexture(GL_TEXTURE0 + unit);
        glBindTexture(target, handle);
        glBindSampler(unit, sampler);
        glUniform1i(l, (int)unit);
    }

    public void Dispose()
    {
        if (Handle != 0)
        {
            glDeleteProgram(Handle);
            Handle = 0;
        }
    }
}
