using static ShaderMod.Pipeline.GlApi;

namespace ShaderMod.Pipeline;

/// <summary>
/// 光影的原生 GL 部分（与游戏引擎无关，可在离线测试里单独运行）：
/// 天空图、云穹图、画天空，以及延迟光照 → 体积光 → 水面与雾 → 泛光 → 调色。
/// 调用前需保存 GL 状态并调用 GlState.SetPassState()，调用后恢复。
/// </summary>
internal sealed unsafe class PostChain : IDisposable
{
    const int SkyMapSize = 256;
    const int BloomLevels = 7;

    readonly Dictionary<string, GlProgram> m_programs = [];
    readonly GlTexture[] m_bloomA = new GlTexture[BloomLevels];
    readonly GlTexture[] m_bloomB = new GlTexture[BloomLevels];
    readonly GlFramebuffer[] m_bloomFbA = new GlFramebuffer[BloomLevels];
    readonly GlFramebuffer[] m_bloomFbB = new GlFramebuffer[BloomLevels];

    GlTexture m_lut, m_noise, m_skyMap, m_cloudDome;
    GlFramebuffer m_skyFb, m_domeFb;
    GlTexture m_deferred, m_fog, m_composite;
    GlFramebuffer m_deferredFb, m_fogFb, m_compositeFb;
    uint m_samplerRaw, m_samplerCompare, m_vao;
    int m_domeBand;
    bool m_domeComplete;

    public GlTexture ShadowDepth { get; private set; }
    public GlTexture SceneDepth { get; private set; }
    public GlTexture WaterDepth { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    public PostChain()
    {
        try
        {
            Create();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    void Create()
    {
        // Derivative 的预计算大气 LUT（256×128×33，半精度）与噪声纹理
        byte[] lut = ShaderSource.ReadGzip("ShaderMod.Data/AtmosphereLut.rgba16f.gz", 256 * 128 * 33 * 8);
        fixed (byte* p = lut)
        {
            m_lut = GlTexture.Create3D(256, 128, 33, GL_RGBA16F, GL_RGBA, GL_HALF_FLOAT, p);
        }
        byte[] noise = ShaderSource.ReadGzip("ShaderMod.Data/Noise2D.rgba8.gz", 256 * 256 * 4);
        fixed (byte* p = noise)
        {
            m_noise = GlTexture.Create2D(256, 256, GL_RGBA8, GL_RGBA, GL_UNSIGNED_BYTE, true, true, p);
        }

        m_skyMap = GlTexture.CreateHdr(SkyMapSize, SkyMapSize);
        m_skyFb = GlFramebuffer.Create(m_skyMap);
        m_cloudDome = GlTexture.CreateHdr(ShaderSettings.CloudDomeWidth, ShaderSettings.CloudDomeHeight, repeatX: true);
        m_domeFb = GlFramebuffer.Create(m_cloudDome);
        ShadowDepth = GlTexture.CreateDepth(ShaderSettings.ShadowMapResolution, ShaderSettings.ShadowMapResolution);

        uint sampler;
        glGenSamplers(1, &sampler);
        m_samplerRaw = sampler;
        SetSampler(m_samplerRaw, false);
        glGenSamplers(1, &sampler);
        m_samplerCompare = sampler;
        SetSampler(m_samplerCompare, true);

        uint vao;
        glGenVertexArrays(1, &vao);
        m_vao = vao;

        string[] quality =
        [
            $"PCF_SAMPLES {ShaderSettings.PcfSamples}u",
            $"CLOUD_CUMULUS_SAMPLES {ShaderSettings.CloudSamples}u",
            $"VOLUMETRIC_FOG_SAMPLES {ShaderSettings.VolumetricFogSamples}",
            $"RAYTRACE_SAMPLES {ShaderSettings.ReflectionSamples}u",
        ];
        foreach (string name in ShaderSource.Programs.Keys)
        {
            var (vertex, fragment) = ShaderSource.ProgramSources(name, quality);
            m_programs[name] = new GlProgram(name, vertex, fragment);
        }
        CheckError("PostChain.Create");
    }

    static void SetSampler(uint sampler, bool compare)
    {
        int filter = (int)(compare ? GL_LINEAR : GL_NEAREST);
        glSamplerParameteri(sampler, GL_TEXTURE_MIN_FILTER, filter);
        glSamplerParameteri(sampler, GL_TEXTURE_MAG_FILTER, filter);
        glSamplerParameteri(sampler, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
        glSamplerParameteri(sampler, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
        if (compare)
        {
            glSamplerParameteri(sampler, GL_TEXTURE_COMPARE_MODE, (int)GL_COMPARE_REF_TO_TEXTURE);
            glSamplerParameteri(sampler, GL_TEXTURE_COMPARE_FUNC, (int)GL_LEQUAL);
        }
    }

    /// <summary>按画面大小（重新）分配中间纹理；大小不变时什么也不做</summary>
    public void Resize(int width, int height)
    {
        if (width == Width && height == Height && SceneDepth != null)
        {
            return;
        }
        DisposeSized();
        Width = width;
        Height = height;
        SceneDepth = GlTexture.CreateDepth(width, height);
        WaterDepth = GlTexture.CreateDepth(width, height);
        m_deferred = GlTexture.CreateHdr(width, height);
        m_deferredFb = GlFramebuffer.Create(m_deferred);
        m_fog = GlTexture.CreateHdr(Math.Max(1, width / 2), Math.Max(1, height / 2));
        m_fogFb = GlFramebuffer.Create(m_fog);
        m_composite = GlTexture.CreateHdr(width, height);
        m_compositeFb = GlFramebuffer.Create(m_composite);
        int w = width, h = height;
        for (int i = 0; i < BloomLevels; i++)
        {
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
            m_bloomA[i] = GlTexture.CreateHdr(w, h);
            m_bloomFbA[i] = GlFramebuffer.Create(m_bloomA[i]);
            m_bloomB[i] = GlTexture.CreateHdr(w, h);
            m_bloomFbB[i] = GlFramebuffer.Create(m_bloomB[i]);
        }
        CheckError("PostChain.Resize");
    }

    GlProgram Begin(string name, FrameUniforms u)
    {
        GlProgram p = m_programs[name];
        p.Use();
        u?.Apply(p);
        p.Texture("skyMap", m_skyMap);
        p.Texture("atmosphereLut", m_lut);
        p.Texture("noisetex", m_noise);
        return p;
    }

    void Draw()
    {
        glBindVertexArray(m_vao);
        glDrawArrays(GL_TRIANGLES, 0, 3);
    }

    /// <summary>画场景之前：天空图（每帧）+ 云穹图的一条（首次全部）</summary>
    public void RenderSky(FrameUniforms u)
    {
        GlProgram p = Begin("SkyCapture", u);
        m_skyFb.Bind();
        Draw();
        CheckError("SkyCapture");

        p = Begin("CloudDome", u);
        p.Set("domeSize", ShaderSettings.CloudDomeWidth, ShaderSettings.CloudDomeHeight);
        m_domeFb.Bind();
        if (m_domeComplete)
        {
            int bandHeight = ShaderSettings.CloudDomeHeight / ShaderSettings.CloudDomeBands;
            glEnable(GL_SCISSOR_TEST);
            glScissor(0, m_domeBand * bandHeight, ShaderSettings.CloudDomeWidth, bandHeight);
            Draw();
            glDisable(GL_SCISSOR_TEST);
            m_domeBand = (m_domeBand + 1) % ShaderSettings.CloudDomeBands;
        }
        else
        {
            Draw();
            m_domeComplete = true;
        }
        CheckError("CloudDome");
    }

    /// <summary>在游戏画天空的位置画 Derivative 天空：画进当前绑定的场景帧缓冲，只覆盖深度为 1（没有地形）的像素</summary>
    public void DrawSky(FrameUniforms u, uint sceneFramebuffer, HeightMap heightMap)
    {
        GlProgram p = Begin("SkyDraw", u);
        p.Set("viewSize", Width, Height);
        p.Texture("cloudDome", m_cloudDome);
        p.Texture("heightMap", heightMap.Texture);
        glBindFramebuffer(GL_FRAMEBUFFER, sceneFramebuffer);
        glViewport(0, 0, Width, Height);
        glEnable(GL_DEPTH_TEST);
        glDepthFunc(GL_LEQUAL);
        glDepthMask(0);
        Draw();
        glDisable(GL_DEPTH_TEST);
        CheckError("SkyDraw");
    }

    /// <summary>场景画完后：延迟光照 → 体积光 → 水面与雾 → 泛光 → 调色，写入 outputFramebuffer（8 位，交给游戏贴屏）</summary>
    public void RenderPost(FrameUniforms u, uint sceneColor, HeightMap heightMap, uint outputFramebuffer)
    {
        // 延迟光照
        GlProgram p = Begin("Deferred", u);
        p.Texture("sceneTex", GL_TEXTURE_2D, sceneColor, m_samplerRaw);
        p.Texture("depthtex1", SceneDepth, m_samplerRaw);
        p.Texture("heightMap", heightMap.Texture);
        p.Texture("cloudDome", m_cloudDome);
        p.Texture("shadowtex0", ShadowDepth, m_samplerRaw);
        p.Texture("shadowtex1", ShadowDepth, m_samplerCompare);
        m_deferredFb.Bind();
        Draw();
        CheckError("Deferred");

        // 体积光（半分辨率）
        p = Begin("VolumetricLight", u);
        p.Texture("depthtex0", WaterDepth, m_samplerRaw);
        p.Texture("shadowtex1", ShadowDepth, m_samplerCompare);
        m_fogFb.Bind();
        Draw();
        CheckError("VolumetricLight");

        // 水面、大气散射、边界雾、合成体积光
        p = Begin("Composite", u);
        p.Texture("deferredTex", m_deferred);
        p.Texture("fogTex", m_fog);
        p.Texture("depthtex0", WaterDepth, m_samplerRaw);
        p.Texture("depthtex1", SceneDepth, m_samplerRaw);
        p.Texture("heightMap", heightMap.Texture);
        p.Texture("cloudDome", m_cloudDome);
        m_compositeFb.Bind();
        Draw();
        CheckError("Composite");

        // 泛光：逐级降采样，每级做水平 + 垂直模糊
        GlTexture source = m_composite;
        for (int i = 0; i < BloomLevels; i++)
        {
            p = m_programs["BloomDown"];
            p.Use();
            p.Texture("sourceTex", source);
            p.Set("targetSize", m_bloomA[i].Width, m_bloomA[i].Height);
            p.Set("firstLevel", i == 0 ? 1f : 0f);
            m_bloomFbA[i].Bind();
            Draw();

            p = m_programs["BloomBlur"];
            p.Use();
            p.Texture("sourceTex", m_bloomA[i]);
            p.Set("blurDirection", 1f, 0f);
            m_bloomFbB[i].Bind();
            Draw();

            p.Use();
            p.Texture("sourceTex", m_bloomB[i]);
            p.Set("blurDirection", 0f, 1f);
            m_bloomFbA[i].Bind();
            Draw();

            source = m_bloomA[i];
        }
        CheckError("Bloom");

        // 调色
        p = Begin("Grade", u);
        p.Texture("compositeTex", m_composite);
        for (int i = 0; i < BloomLevels; i++)
        {
            p.Texture("bloomTex" + (i + 1), m_bloomA[i]);
        }
        glBindFramebuffer(GL_FRAMEBUFFER, outputFramebuffer);
        glViewport(0, 0, Width, Height);
        Draw();
        CheckError("Grade");
    }

    void DisposeSized()
    {
        SceneDepth?.Dispose();
        SceneDepth = null;
        WaterDepth?.Dispose();
        WaterDepth = null;
        m_deferredFb?.Dispose();
        m_deferred?.Dispose();
        m_fogFb?.Dispose();
        m_fog?.Dispose();
        m_compositeFb?.Dispose();
        m_composite?.Dispose();
        for (int i = 0; i < BloomLevels; i++)
        {
            m_bloomFbA[i]?.Dispose();
            m_bloomA[i]?.Dispose();
            m_bloomFbB[i]?.Dispose();
            m_bloomB[i]?.Dispose();
            m_bloomFbA[i] = m_bloomFbB[i] = null;
            m_bloomA[i] = m_bloomB[i] = null;
        }
        Width = Height = 0;
    }

    public void Dispose()
    {
        DisposeSized();
        foreach (GlProgram p in m_programs.Values)
        {
            p.Dispose();
        }
        m_programs.Clear();
        m_skyFb?.Dispose();
        m_domeFb?.Dispose();
        m_skyMap?.Dispose();
        m_cloudDome?.Dispose();
        m_lut?.Dispose();
        m_noise?.Dispose();
        ShadowDepth?.Dispose();
        if (m_samplerRaw != 0)
        {
            uint s = m_samplerRaw;
            glDeleteSamplers(1, &s);
            m_samplerRaw = 0;
        }
        if (m_samplerCompare != 0)
        {
            uint s = m_samplerCompare;
            glDeleteSamplers(1, &s);
            m_samplerCompare = 0;
        }
        if (m_vao != 0)
        {
            uint v = m_vao;
            glDeleteVertexArrays(1, &v);
            m_vao = 0;
        }
    }
}
