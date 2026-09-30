using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using Engine.Graphics;
using Game;
using HarmonyLib;

namespace ShaderMod;

/// <summary>
/// 全屏后处理：亮部提取 → 1/4 分辨率高斯模糊（水平 + 垂直）→ 合成（泛光 + 色调曲线 + 鲜艳度 + 对比 + 暗角）。
/// 输入是游戏画好场景的渲染目标，输出是同尺寸的结果渲染目标。
/// </summary>
internal static class PostProcessor
{
    // BSL 风格默认参数
    const float BloomThreshold = 0.72f;
    const float BloomKnee = 0.25f;
    const float BloomStrength = 0.35f;
    const float Exposure = 1.0f;
    const float Saturation = 1.10f;
    const float Contrast = 0.22f;
    const float Vignette = 0.45f;

    static Shader s_extractShader;
    static Shader s_blurShader;
    static Shader s_compositeShader;
    static RenderTarget2D s_result;
    static RenderTarget2D s_bloomA;
    static RenderTarget2D s_bloomB;

    /// <summary>全屏两个三角形。纹理坐标 v=0 为画面顶部（与游戏渲染目标的约定一致）</summary>
    static readonly VertexPositionColorTexture[] s_quad =
    [
        Vertex(-1f, 1f, 0f, 0f), Vertex(1f, 1f, 1f, 0f), Vertex(1f, -1f, 1f, 1f),
        Vertex(-1f, 1f, 0f, 0f), Vertex(1f, -1f, 1f, 1f), Vertex(-1f, -1f, 0f, 1f),
    ];

    static VertexPositionColorTexture Vertex(float x, float y, float u, float v) =>
        new() { Position = new Vector3(x, y, 0f), Color = Color.White, TexCoord = new Vector2(u, v) };

    public static RenderTarget2D Run(RenderTarget2D scene)
    {
        EnsureShaders();
        int width = scene.Width;
        int height = scene.Height;
        int bloomWidth = Math.Max(1, width / 4);
        int bloomHeight = Math.Max(1, height / 4);
        EnsureTarget(ref s_result, width, height);
        EnsureTarget(ref s_bloomA, bloomWidth, bloomHeight);
        EnsureTarget(ref s_bloomB, bloomWidth, bloomHeight);

        RenderTarget2D previousTarget = Display.RenderTarget;
        BlendState previousBlend = Display.BlendState;
        DepthStencilState previousDepth = Display.DepthStencilState;
        RasterizerState previousRasterizer = Display.RasterizerState;
        try
        {
            Display.BlendState = BlendState.Opaque;
            Display.DepthStencilState = DepthStencilState.None;
            Display.RasterizerState = RasterizerState.CullNoneScissor;

            // 1. 亮部提取 + 降采样：场景 → bloomA
            Display.RenderTarget = s_bloomA;
            SetTexture(s_extractShader, "u_texture", "u_samplerState", scene);
            s_extractShader.GetParameter("u_sourceTexelSize", true)?.SetValue(new Vector2(1f / width, 1f / height));
            s_extractShader.GetParameter("u_threshold", true)?.SetValue(BloomThreshold);
            s_extractShader.GetParameter("u_knee", true)?.SetValue(BloomKnee);
            DrawQuad(s_extractShader);

            // 2. 水平模糊：bloomA → bloomB
            Display.RenderTarget = s_bloomB;
            SetTexture(s_blurShader, "u_texture", "u_samplerState", s_bloomA);
            s_blurShader.GetParameter("u_direction", true)?.SetValue(new Vector2(1f / bloomWidth, 0f));
            DrawQuad(s_blurShader);

            // 3. 垂直模糊：bloomB → bloomA
            Display.RenderTarget = s_bloomA;
            SetTexture(s_blurShader, "u_texture", "u_samplerState", s_bloomB);
            s_blurShader.GetParameter("u_direction", true)?.SetValue(new Vector2(0f, 1f / bloomHeight));
            DrawQuad(s_blurShader);

            // 4. 合成：场景 + 泛光 → 结果
            Display.RenderTarget = s_result;
            SetTexture(s_compositeShader, "u_scene", "u_sceneSamplerState", scene);
            SetTexture(s_compositeShader, "u_bloom", "u_bloomSamplerState", s_bloomA);
            s_compositeShader.GetParameter("u_bloomStrength", true)?.SetValue(BloomStrength);
            s_compositeShader.GetParameter("u_exposure", true)?.SetValue(Exposure);
            s_compositeShader.GetParameter("u_saturation", true)?.SetValue(Saturation);
            s_compositeShader.GetParameter("u_contrast", true)?.SetValue(Contrast);
            s_compositeShader.GetParameter("u_vignette", true)?.SetValue(Vignette);
            DrawQuad(s_compositeShader);
        }
        finally
        {
            Display.RenderTarget = previousTarget;
            Display.BlendState = previousBlend;
            Display.DepthStencilState = previousDepth;
            Display.RasterizerState = previousRasterizer;
        }
        return s_result;
    }

    public static void DisposeTargets()
    {
        Utilities.Dispose(ref s_result);
        Utilities.Dispose(ref s_bloomA);
        Utilities.Dispose(ref s_bloomB);
    }

    static void DrawQuad(Shader shader) =>
        Display.DrawUser(PrimitiveType.TriangleList, shader, VertexPositionColorTexture.VertexDeclaration, s_quad, 0, s_quad.Length);

    static void SetTexture(Shader shader, string textureName, string samplerName, Texture2D texture)
    {
        shader.GetParameter(textureName, true)?.SetValue(texture);
        shader.GetParameter(samplerName, true)?.SetValue(SamplerState.LinearClamp);
    }

    static void EnsureTarget(ref RenderTarget2D target, int width, int height)
    {
        if (target != null && target.Width == width && target.Height == height)
        {
            return;
        }
        Utilities.Dispose(ref target);
        target = new RenderTarget2D(width, height, 1, ColorFormat.Rgba8888, DepthFormat.None);
    }

    static void EnsureShaders()
    {
        if (s_compositeShader != null)
        {
            return;
        }
        string vertex = ReadEmbedded("Post.vsh");
        s_extractShader = new Shader(vertex, ReadEmbedded("BloomExtract.psh"));
        s_blurShader = new Shader(vertex, ReadEmbedded("Blur.psh"));
        s_compositeShader = new Shader(vertex, ReadEmbedded("Composite.psh"));
    }

    internal static string ReadEmbedded(string fileName)
    {
        string resourceName = "ShaderMod.PostShaders." + fileName;
        using Stream stream = typeof(PostProcessor).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded shader not found: {resourceName}");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>
/// 用 HarmonyX 接入 ViewWidget：
/// - SetupScalingRenderTarget：光影开启时，始终把场景画进渲染目标（原版只在降分辨率时这样做）；
/// - ApplyScalingRenderTarget：贴到屏幕前先做后处理，并把结果临时交给原版代码贴屏（分屏、缩放、颜色变换都由原版处理）。
/// 任何一步出错都只记一次日志并永久关闭后处理，画面退回原版。
/// </summary>
internal static class PostProcessPatches
{
    static FieldInfo s_renderTargetField;
    static bool s_failed;

    public static bool Installed { get; private set; }

    static bool IsActive => Installed && !s_failed && ShaderModLoader.Enabled;

    public static void TryInstall()
    {
        try
        {
            InstallCore();
        }
        catch (Exception e)
        {
            Log.Warning($"[ShaderMod] Post-processing disabled, failed to patch ViewWidget: {e}");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void InstallCore()
    {
        MethodInfo setup = AccessTools.Method(typeof(ViewWidget), "SetupScalingRenderTarget");
        MethodInfo apply = AccessTools.Method(typeof(ViewWidget), "ApplyScalingRenderTarget");
        s_renderTargetField = AccessTools.Field(typeof(ViewWidget), "m_scalingRenderTarget");
        if (setup == null || apply == null || s_renderTargetField == null)
        {
            Log.Warning("[ShaderMod] Post-processing disabled: ViewWidget members not found in this game version.");
            return;
        }
        Harmony harmony = new("ShaderMod.PostProcess");
        harmony.Patch(setup, prefix: new HarmonyMethod(typeof(PostProcessPatches), nameof(SetupPrefix)));
        harmony.Patch(
            apply,
            prefix: new HarmonyMethod(typeof(PostProcessPatches), nameof(ApplyPrefix)),
            postfix: new HarmonyMethod(typeof(PostProcessPatches), nameof(ApplyPostfix))
        );
        Installed = true;
    }

    /// <returns>false = 已由本模组设置好渲染目标，跳过原版</returns>
    public static bool SetupPrefix(ViewWidget __instance)
    {
        if (!IsActive)
        {
            return true;
        }
        try
        {
            return !SetupCore(__instance);
        }
        catch (Exception e)
        {
            Fail(e);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static bool SetupCore(ViewWidget widget)
    {
        // 与原版相同的分辨率系数
        float scale = SettingsManager.ResolutionMode == ResolutionMode.Low ? 0.5f
            : SettingsManager.ResolutionMode != ResolutionMode.Medium ? 1f : 0.75f;
        float scaleX = widget.GlobalTransform.Right.Length();
        float scaleY = widget.GlobalTransform.Up.Length();
        int width = (int)MathF.Round(widget.ActualSize.X * scaleX * scale);
        int height = (int)MathF.Round(widget.ActualSize.Y * scaleY * scale);
        if (width <= 0 || height <= 0)
        {
            return false;
        }
        RenderTarget2D target = s_renderTargetField.GetValue(widget) as RenderTarget2D;
        if (target == null || target.Width != width || target.Height != height)
        {
            target?.Dispose();
            target = new RenderTarget2D(width, height, 1, ColorFormat.Rgba8888, DepthFormat.Depth24Stencil8);
            s_renderTargetField.SetValue(widget, target);
        }
        Display.RenderTarget = target;
        Display.Clear(Color.Black, 1f, 0);
        return true;
    }

    public static void ApplyPrefix(ViewWidget __instance, out RenderTarget2D __state)
    {
        __state = null;
        if (!IsActive)
        {
            return;
        }
        try
        {
            __state = ApplyCore(__instance);
        }
        catch (Exception e)
        {
            __state = null;
            Fail(e);
        }
    }

    /// <returns>被临时替换掉的原场景渲染目标（Postfix 负责换回）；null 表示未替换</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    static RenderTarget2D ApplyCore(ViewWidget widget)
    {
        if (s_renderTargetField.GetValue(widget) is not RenderTarget2D scene)
        {
            return null;
        }
        RenderTarget2D result = PostProcessor.Run(scene);
        s_renderTargetField.SetValue(widget, result);
        return scene;
    }

    public static void ApplyPostfix(ViewWidget __instance, RenderTarget2D __state)
    {
        if (__state == null)
        {
            return;
        }
        try
        {
            s_renderTargetField.SetValue(__instance, __state);
        }
        catch (Exception e)
        {
            Fail(e);
        }
    }

    static void Fail(Exception e)
    {
        if (!s_failed)
        {
            s_failed = true;
            Log.Error($"[ShaderMod] Post-processing failed and has been turned off for this session: {e}");
        }
    }
}
