using Engine;
using Engine.Graphics;
using Game;
using GameEntitySystem;
using static ShaderMod.Pipeline.GlApi;

namespace ShaderMod.Pipeline;

/// <summary>
/// 把 Derivative 光影接进游戏的一帧：
/// 1. 画场景前（ViewWidget.SetupScalingRenderTarget）：更新 uniform、高度图、天空图、云穹图，画太阳阴影贴图，
///    然后让游戏把场景画进我们的渲染目标（深度挂成可采样的纹理）；
/// 2. 游戏画天空时（SubsystemSky.Draw，顺序 5）：改画 Derivative 的天空、云、星星和月亮；
/// 3. 贴屏前（ViewWidget.ApplyScalingRenderTarget）：画水面深度，再跑后处理链，结果交给原版贴屏代码。
/// 地形着色器：关闭原版距离雾、不画水（水面在后处理里重画）。
/// </summary>
internal sealed unsafe class GamePipeline : IDisposable
{
    public static GamePipeline Instance { get; private set; }

    readonly FrameUniforms m_uniforms = new();
    readonly PostChain m_chain;
    readonly HeightMap m_heightMap;
    readonly Shader m_shadowShader;
    readonly Shader m_waterShader;
    RenderTarget2D m_shadowTarget;
    RenderTarget2D m_waterTarget;
    RenderTarget2D m_resultTarget;
    RenderTarget2D m_attachedScene;
    Project m_project;
    ViewWidget m_widget;
    Vector4 m_waterSlots = new(-1f);
    double m_lastFrameTime;

    /// <summary>本帧的场景是否由光影管线接管（Setup 成功后为真，Apply 后清除）</summary>
    public bool FrameActive { get; private set; }
    public RenderTarget2D SceneTarget { get; private set; }

    GamePipeline()
    {
        GlApi.Load();
        GlState state = GlState.Save();
        try
        {
            GlState.SetPassState();
            m_chain = new PostChain();
            m_heightMap = new HeightMap();
        }
        finally
        {
            state.Restore();
        }
        m_shadowShader = new Shader(
            ShaderSource.ReadText("ShaderMod.EngineShaders/ShadowTerrain.vsh"),
            ShaderSource.ReadText("ShaderMod.EngineShaders/ShadowTerrain.psh"));
        m_waterShader = new Shader(
            ShaderSource.ReadText("ShaderMod.EngineShaders/WaterMask.vsh"),
            ShaderSource.ReadText("ShaderMod.EngineShaders/WaterMask.psh"));
        Display.DeviceReset += OnDeviceReset;
    }

    public static GamePipeline GetOrCreate() => Instance ??= new GamePipeline();

    public static void DisposeInstance()
    {
        Instance?.Dispose();
        Instance = null;
    }

    static void OnDeviceReset() => DisposeInstance();

    // ------------------------------------------------------------------ 1. 画场景之前

    /// <summary>返回 true 表示已设置好渲染目标（跳过原版 SetupScalingRenderTarget）</summary>
    public bool Setup(ViewWidget widget, ref RenderTarget2D scalingTarget)
    {
        FrameActive = false;
        float scale = SettingsManager.ResolutionMode == ResolutionMode.Low ? 0.5f
            : SettingsManager.ResolutionMode != ResolutionMode.Medium ? 1f : 0.75f;
        int width = (int)MathF.Round(widget.ActualSize.X * widget.GlobalTransform.Right.Length() * scale);
        int height = (int)MathF.Round(widget.ActualSize.Y * widget.GlobalTransform.Up.Length() * scale);
        if (width <= 0 || height <= 0)
        {
            return false;
        }
        m_widget = widget;
        Project project = widget.GameWidget.SubsystemGameWidgets.Project;
        Camera camera = widget.GameWidget.ActiveCamera;
        if (!ReferenceEquals(project, m_project))
        {
            m_project = project;
            m_waterSlots = FindWaterSlots();
        }
        SubsystemTerrain subsystemTerrain = project.FindSubsystem<SubsystemTerrain>(true);

        // 先准备好场景渲染目标：相机的投影矩阵会按"是否画进渲染目标"缓存，必须在读取矩阵之前建好
        if (scalingTarget == null || scalingTarget.Width != width || scalingTarget.Height != height)
        {
            scalingTarget?.Dispose();
            scalingTarget = new RenderTarget2D(width, height, 1, ColorFormat.Rgba8888, DepthFormat.Depth24Stencil8);
        }
        SceneTarget = scalingTarget;
        EnsureTargets(width, height);

        m_uniforms.Update(camera, project, width, height);
        double now = Time.RealTime;
        float dt = m_lastFrameTime > 0 ? (float)Math.Clamp(now - m_lastFrameTime, 0.0, 0.25) : 1f;
        m_lastFrameTime = now;
        m_uniforms.UpdateEyeSkylight(HeightMap.SkyExposureAt(subsystemTerrain.Terrain, m_uniforms.CameraPosition), dt);

        GlApi.ClearErrors();
        GlState state = GlState.Save();
        try
        {
            GlState.SetPassState();
            m_heightMap.Update(subsystemTerrain.Terrain, m_uniforms.CameraPosition);
            m_chain.RenderSky(m_uniforms);
        }
        finally
        {
            state.Restore();
        }

        RenderShadowMap(subsystemTerrain.TerrainRenderer, subsystemTerrain.Terrain);

        Display.RenderTarget = scalingTarget;
        Display.Clear(Color.Black, 1f, 0);
        FrameActive = true;
        return true;
    }

    void EnsureTargets(int width, int height)
    {
        GlState state = GlState.Save();
        try
        {
            if (m_chain.Width != width || m_chain.Height != height)
            {
                m_chain.Resize(width, height);
                m_attachedScene = null;
                Utilities.Dispose(ref m_waterTarget);
                Utilities.Dispose(ref m_resultTarget);
            }
            if (!ReferenceEquals(m_attachedScene, SceneTarget))
            {
                // 把可采样的深度纹理挂到游戏场景渲染目标上，替换它原来的深度缓冲
                GlFramebuffer.Wrap(EngineAccess.FrameBuffer(SceneTarget), width, height).AttachDepth(m_chain.SceneDepth);
                m_attachedScene = SceneTarget;
            }
            if (m_waterTarget == null)
            {
                m_waterTarget = new RenderTarget2D(width, height, 1, ColorFormat.Rgba8888, DepthFormat.None);
                GlFramebuffer.Wrap(EngineAccess.FrameBuffer(m_waterTarget), width, height).AttachDepth(m_chain.WaterDepth);
            }
            m_resultTarget ??= new RenderTarget2D(width, height, 1, ColorFormat.Rgba8888, DepthFormat.None);
            if (m_shadowTarget == null)
            {
                int size = ShaderSettings.ShadowMapResolution;
                m_shadowTarget = new RenderTarget2D(size, size, 1, ColorFormat.Rgba8888, DepthFormat.None);
                GlFramebuffer.Wrap(EngineAccess.FrameBuffer(m_shadowTarget), size, size).AttachDepth(m_chain.ShadowDepth);
            }
            CheckError("EnsureTargets");
        }
        finally
        {
            state.Restore();
        }
    }

    /// <summary>用引擎把相机周围的地形（不透明 + 镂空）画进阴影贴图</summary>
    void RenderShadowMap(TerrainRenderer renderer, Terrain terrain)
    {
        Display.RenderTarget = m_shadowTarget;
        Display.Clear(Color.White, 1f, 0);
        Display.BlendState = BlendState.Opaque;
        Display.DepthStencilState = DepthStencilState.Default;
        Display.RasterizerState = RasterizerState.CullNone;

        Vector3 camera = m_uniforms.CameraPosition;
        Vector3 origin = new(MathF.Floor(camera.X), 0f, MathF.Floor(camera.Z));
        m_shadowShader.GetParameter("u_shadowMatrix", true)?.SetValue(m_uniforms.ShadowMatrixForOrigin(origin));
        m_shadowShader.GetParameter("u_origin", true)?.SetValue(origin.XZ);
        m_shadowShader.GetParameter("u_samplerState", true)?.SetValue(SamplerState.PointClamp);

        float range = ShaderSettings.ShadowDistance * 1.2f + 16f;
        float rangeSquared = range * range;
        foreach (TerrainChunk chunk in terrain.AllocatedChunks)
        {
            if (chunk == null)
            {
                continue;
            }
            Vector2 center = new(chunk.Origin.X + 8f, chunk.Origin.Y + 8f);
            if (Vector2.DistanceSquared(center, camera.XZ) > rangeSquared)
            {
                continue;
            }
            // 子集 0~4：不透明（按朝向分组），子集 5：镂空（树叶、草等）
            EngineAccess.DrawChunkSubsets(renderer, m_shadowShader, chunk, 0b111111);
        }
    }

    // ------------------------------------------------------------------ 2. 画天空

    /// <summary>返回 true 表示已画好天空（跳过原版）</summary>
    public bool DrawSky(SubsystemSky sky)
    {
        if (!FrameActive || !EngineAccess.IsSkyVisible(sky))
        {
            return false;
        }
        GlState state = GlState.Save();
        try
        {
            GlState.SetPassState();
            m_chain.DrawSky(m_uniforms, (uint)EngineAccess.FrameBuffer(SceneTarget), m_heightMap);
        }
        finally
        {
            state.Restore();
        }
        return true;
    }

    // ------------------------------------------------------------------ 3. 贴屏之前

    /// <summary>返回交给原版贴屏的结果渲染目标；null 表示本帧未接管</summary>
    public RenderTarget2D Apply()
    {
        if (!FrameActive)
        {
            return null;
        }
        FrameActive = false;
        SubsystemTerrain subsystemTerrain = m_project.FindSubsystem<SubsystemTerrain>(true);
        SubsystemSky sky = m_project.FindSubsystem<SubsystemSky>(true);

        RenderWaterDepth(subsystemTerrain.TerrainRenderer, sky);

        GlState state = GlState.Save();
        try
        {
            GlState.SetPassState();
            m_chain.RenderPost(
                m_uniforms,
                EngineAccess.TextureHandle(SceneTarget),
                m_heightMap,
                (uint)EngineAccess.FrameBuffer(m_resultTarget));
        }
        finally
        {
            state.Restore();
        }
        return m_resultTarget;
    }

    /// <summary>水面深度：先复制场景深度，再用引擎画一遍半透明子集里的水（与原版变换完全相同）</summary>
    void RenderWaterDepth(TerrainRenderer renderer, SubsystemSky sky)
    {
        GlState state = GlState.Save();
        try
        {
            glBindFramebuffer(GL_READ_FRAMEBUFFER, (uint)EngineAccess.FrameBuffer(SceneTarget));
            glBindFramebuffer(GL_DRAW_FRAMEBUFFER, (uint)EngineAccess.FrameBuffer(m_waterTarget));
            glBlitFramebuffer(0, 0, m_chain.Width, m_chain.Height, 0, 0, m_chain.Width, m_chain.Height,
                GL_DEPTH_BUFFER_BIT | GL_STENCIL_BUFFER_BIT, GL_NEAREST);
            CheckError("BlitDepth");
        }
        finally
        {
            state.Restore();
        }

        RenderTarget2D previous = Display.RenderTarget;
        try
        {
            Display.RenderTarget = m_waterTarget;
            Display.BlendState = BlendState.Opaque;
            Display.DepthStencilState = DepthStencilState.Default;
            Display.RasterizerState = RasterizerState.CullNone;

            Vector3 viewPosition = m_uniforms.CameraPosition;
            Vector3 v = new(MathF.Floor(viewPosition.X), 0f, MathF.Floor(viewPosition.Z));
            Matrix viewProjection = Matrix.CreateTranslation(v - viewPosition) * m_uniforms.ModelView * m_uniforms.RawProjection;
            m_waterShader.GetParameter("u_viewProjectionMatrix", true)?.SetValue(viewProjection);
            m_waterShader.GetParameter("u_origin", true)?.SetValue(v.XZ);
            m_waterShader.GetParameter("u_waterSlots", true)?.SetValue(m_waterSlots);
            m_waterShader.GetParameter("u_samplerState", true)?.SetValue(SamplerState.PointClamp);
            foreach (TerrainChunk chunk in EngineAccess.ChunksToDraw(renderer))
            {
                EngineAccess.DrawChunkSubsets(renderer, m_waterShader, chunk, 64);
            }
        }
        finally
        {
            Display.RenderTarget = previous;
        }
    }

    // ------------------------------------------------------------------ 地形着色器参数

    /// <summary>每帧设置原版地形着色器的额外参数（管线未接管时全部为 0，与原版一致）</summary>
    public static void ApplyTerrainParameters(Project project, bool active, Vector4 waterSlots)
    {
        TerrainRenderer renderer = project?.FindSubsystem<SubsystemTerrain>(false)?.TerrainRenderer;
        if (renderer == null)
        {
            return;
        }
        foreach (Shader shader in EngineAccess.TerrainShaders(renderer))
        {
            shader.GetParameter("u_vanillaFogOff", true)?.SetValue(active ? 1f : 0f);
            shader.GetParameter("u_hideWater", true)?.SetValue(active ? 1f : 0f);
            shader.GetParameter("u_waterSlots", true)?.SetValue(waterSlots);
        }
    }

    public Vector4 WaterSlots => m_waterSlots;

    /// <summary>水方块在地形图集里的贴图格号（最多 4 个）</summary>
    static Vector4 FindWaterSlots()
    {
        List<float> slots = [];
        foreach (Block block in BlocksManager.Blocks)
        {
            if (block is WaterBlock && block.DefaultTextureSlot >= 0 && !slots.Contains(block.DefaultTextureSlot))
            {
                slots.Add(block.DefaultTextureSlot);
            }
        }
        while (slots.Count < 4)
        {
            slots.Add(-1f);
        }
        return new Vector4(slots[0], slots[1], slots[2], slots[3]);
    }

    public void Dispose()
    {
        Display.DeviceReset -= OnDeviceReset;
        FrameActive = false;
        // 场景渲染目标上挂着本模组的深度纹理：一起释放，并让游戏下次重新创建
        if (SceneTarget != null)
        {
            if (m_widget != null && ReferenceEquals(EngineAccess.GetScalingTarget(m_widget), SceneTarget))
            {
                EngineAccess.SetScalingTarget(m_widget, null);
            }
            SceneTarget.Dispose();
            SceneTarget = null;
        }
        m_widget = null;
        Utilities.Dispose(ref m_shadowTarget);
        Utilities.Dispose(ref m_waterTarget);
        Utilities.Dispose(ref m_resultTarget);
        m_shadowShader?.Dispose();
        m_waterShader?.Dispose();
        if (GlApi.IsLoaded)
        {
            GlState state = GlState.Save();
            try
            {
                m_chain?.Dispose();
                m_heightMap?.Dispose();
            }
            finally
            {
                state.Restore();
            }
        }
    }
}
