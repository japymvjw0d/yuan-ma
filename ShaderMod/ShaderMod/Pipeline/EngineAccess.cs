using System.Reflection;
using Engine.Graphics;
using Game;

namespace ShaderMod.Pipeline;

/// <summary>
/// 通过反射访问引擎内部成员（不同 API 版本里可能是字段或属性、公开或非公开），找不到时抛出异常，由调用方回退到原版画面。
/// </summary>
internal static class EngineAccess
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    static readonly FieldInfo s_frameBuffer = typeof(RenderTarget2D).GetField("m_frameBuffer", All);
    static readonly FieldInfo s_texture = typeof(Texture2D).GetField("m_texture", All);
    static readonly FieldInfo s_chunksToDraw = typeof(TerrainRenderer).GetField("m_chunksToDraw", All);
    static readonly FieldInfo s_scalingTarget = typeof(ViewWidget).GetField("m_scalingRenderTarget", All);
    static readonly FieldInfo s_viewIsSkyVisible = typeof(SubsystemSky).GetField("m_viewIsSkyVisible", All);
    static readonly MethodInfo s_drawSubsets = FindDrawSubsets();
    static object[] s_drawArgs;
    static readonly FieldInfo[] s_terrainShaderFields = new[] { "m_opaqueShader", "m_alphaTestedShader", "m_transparentShader" }
        .Select(name => typeof(TerrainRenderer).GetField(name, All))
        .ToArray();

    public static int FrameBuffer(RenderTarget2D target) =>
        Convert.ToInt32((s_frameBuffer ?? throw new MissingFieldException("RenderTarget2D.m_frameBuffer")).GetValue(target));

    public static RenderTarget2D GetScalingTarget(ViewWidget widget) => s_scalingTarget?.GetValue(widget) as RenderTarget2D;

    public static void SetScalingTarget(ViewWidget widget, RenderTarget2D target) => s_scalingTarget?.SetValue(widget, target);

    public static uint TextureHandle(Texture2D texture) =>
        (uint)Convert.ToInt32((s_texture ?? throw new MissingFieldException("Texture2D.m_texture")).GetValue(texture));

    /// <summary>本帧要画的地形区块（引擎视锥剔除后的结果）</summary>
    public static IEnumerable<TerrainChunk> ChunksToDraw(TerrainRenderer renderer)
    {
        object value = (s_chunksToDraw ?? throw new MissingFieldException("TerrainRenderer.m_chunksToDraw")).GetValue(renderer);
        return value as IEnumerable<TerrainChunk> ?? throw new InvalidCastException("m_chunksToDraw");
    }

    public static bool IsSkyVisible(SubsystemSky sky) =>
        s_viewIsSkyVisible == null || (bool)s_viewIsSkyVisible.GetValue(sky);

    public static IEnumerable<Shader> TerrainShaders(TerrainRenderer renderer)
    {
        foreach (FieldInfo field in s_terrainShaderFields)
        {
            if (field?.GetValue(field.IsStatic ? null : renderer) is Shader shader)
            {
                yield return shader;
            }
        }
    }

    static MethodInfo FindDrawSubsets()
    {
        foreach (MethodInfo m in typeof(TerrainRenderer).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (m.Name != "DrawTerrainChunkGeometrySubsets")
            {
                continue;
            }
            ParameterInfo[] p = m.GetParameters();
            if (p.Length >= 3 && p[0].ParameterType == typeof(Shader) && p[1].ParameterType == typeof(TerrainChunk) && p[2].ParameterType == typeof(int)
                && (p.Length == 3 || (p.Length == 4 && p[3].ParameterType == typeof(bool))))
            {
                return m;
            }
        }
        return null;
    }

    /// <summary>用引擎的方法画一个区块的指定子集（与原版地形绘制共用顶点缓冲和贴图）</summary>
    public static void DrawChunkSubsets(TerrainRenderer renderer, Shader shader, TerrainChunk chunk, int subsetsMask)
    {
        MethodInfo method = s_drawSubsets ?? throw new MissingMethodException("TerrainRenderer.DrawTerrainChunkGeometrySubsets");
        s_drawArgs ??= method.GetParameters().Length == 4 ? new object[4] : new object[3];
        s_drawArgs[0] = shader;
        s_drawArgs[1] = chunk;
        s_drawArgs[2] = subsetsMask;
        if (s_drawArgs.Length == 4)
        {
            s_drawArgs[3] = true;
        }
        method.Invoke(renderer, s_drawArgs);
        s_drawArgs[1] = null;
    }
}
