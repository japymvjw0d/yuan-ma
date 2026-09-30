using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using Engine.Graphics;
using Game;
using GameEntitySystem;

namespace ShaderMod;

/// <summary>太阳方向计算，与 SubsystemSky.DrawSunAndMoon / QueueCelestialBody 画太阳的方式一致</summary>
internal static class SunMath
{
    public static Vector3 SunDirection(float timeOfDay, float midday, float seasonAngle)
    {
        float angle = 2f * MathF.PI * (timeOfDay - midday);
        Matrix m = Matrix.CreateRotationZ(-angle) * Matrix.CreateRotationX(seasonAngle);
        return Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, m));
    }
}

/// <summary>
/// 每帧把光影参数写入地形的三个着色器。
/// 着色器字段用反射获取（兼容静态/实例字段两种写法），参数用 GetParameter(name, true) 获取：
/// 如果地形着色器被别的模组替换、没有这些参数，就自动跳过。
/// </summary>
internal static class TerrainUniforms
{
    static readonly string[] ShaderFieldNames = ["m_opaqueShader", "m_alphaTestedShader", "m_transparentShader"];
    static readonly FieldInfo[] s_shaderFields = ShaderFieldNames
        .Select(name => typeof(TerrainRenderer).GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
        .ToArray();

    static Project s_project;
    static SubsystemSky s_sky;
    static SubsystemTimeOfDay s_timeOfDay;
    static SubsystemWeather s_weather;
    static SubsystemTime s_time;
    static SubsystemTerrain s_terrain;
    static bool s_seasonAngleUnavailable;

    public static void Reset()
    {
        s_project = null;
        s_sky = null;
        s_timeOfDay = null;
        s_weather = null;
        s_time = null;
        s_terrain = null;
    }

    public static void Update(Project project, bool enabled)
    {
        if (project == null)
        {
            return;
        }
        if (!ReferenceEquals(project, s_project))
        {
            s_project = project;
            s_sky = project.FindSubsystem<SubsystemSky>(false);
            s_timeOfDay = project.FindSubsystem<SubsystemTimeOfDay>(false);
            s_weather = project.FindSubsystem<SubsystemWeather>(false);
            s_time = project.FindSubsystem<SubsystemTime>(false);
            s_terrain = project.FindSubsystem<SubsystemTerrain>(false);
        }
        TerrainRenderer renderer = s_terrain?.TerrainRenderer;
        if (renderer == null || s_sky == null || s_timeOfDay == null)
        {
            return;
        }

        Vector3 sunDirection = SunMath.SunDirection(s_timeOfDay.TimeOfDay, s_timeOfDay.Midday, GetSeasonAngle(s_sky));
        float daylight = s_sky.SkyLightIntensity;
        float rain = s_weather?.PrecipitationIntensity ?? 0f;
        float time = (float)((s_time?.GameTime ?? 0.0) % 3600.0);
        float strength = enabled ? 1f : 0f;

        foreach (FieldInfo field in s_shaderFields)
        {
            if (field?.GetValue(field.IsStatic ? null : renderer) is not Shader shader)
            {
                continue;
            }
            shader.GetParameter("u_shaderStrength", true)?.SetValue(strength);
            shader.GetParameter("u_sunDirection", true)?.SetValue(sunDirection);
            shader.GetParameter("u_daylight", true)?.SetValue(daylight);
            shader.GetParameter("u_rain", true)?.SetValue(rain);
            shader.GetParameter("u_time", true)?.SetValue(time);
        }
    }

    /// <summary>季节倾角（让光照方向与天上的太阳一致）；取不到时按 0 处理</summary>
    static float GetSeasonAngle(SubsystemSky sky)
    {
        if (s_seasonAngleUnavailable)
        {
            return 0f;
        }
        try
        {
            return SeasonAngleCore(sky);
        }
        catch (Exception)
        {
            // 游戏版本中该成员不存在或签名不同 —— 不再尝试
            s_seasonAngleUnavailable = true;
            return 0f;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static float SeasonAngleCore(SubsystemSky sky) => sky.CalculateSeasonAngle();
}
