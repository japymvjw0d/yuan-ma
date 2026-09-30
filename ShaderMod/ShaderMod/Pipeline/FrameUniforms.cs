using System.Runtime.CompilerServices;
using Engine;
using Engine.Graphics;
using Game;
using GameEntitySystem;

namespace ShaderMod.Pipeline;

/// <summary>太阳方向计算，与 SubsystemSky 画太阳的方式一致</summary>
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
/// 每帧计算与 Iris 同名的 uniform（gbufferModelView、shadowModelView、worldSunVector……），
/// 以及本模组自己的参数。矩阵保持引擎的行向量约定，上传时着色器里用 m * v。
/// </summary>
internal sealed class FrameUniforms
{
    // 相机
    public Matrix ModelView, ModelViewInverse, Projection, ProjectionInverse;
    /// <summary>相机原始投影矩阵（引擎着色器用）</summary>
    public Matrix RawProjection;
    public Vector3 CameraPosition;
    public float Near, Far;
    public int Width, Height;

    // 阴影
    public Matrix ShadowModelView, ShadowProjection, ShadowProjectionInverse;
    public Vector3 ShadowCenter;

    // 天空与天气
    public Vector3 SunVector, LightVector;
    public float Wetness, FrameTimeCounter, WorldTimeCounter, MoonPhase;
    public float TimeNoon, TimeMidnight, TimeSunrise, TimeSunset, MeWeight;
    public float EyeSkylightFix = 1f, VolFogDensity;
    public Vector3 VolFogWind;
    public float RenderDistance, LightingStrength;
    public bool EyeInWater;
    public int FrameCounter;
    public float SkyLightIntensity;

    static bool s_seasonAngleUnavailable;

    public void Update(Camera camera, Project project, int width, int height)
    {
        Width = width;
        Height = height;
        FrameCounter++;

        SubsystemSky sky = project.FindSubsystem<SubsystemSky>(true);
        SubsystemTimeOfDay timeOfDay = project.FindSubsystem<SubsystemTimeOfDay>(true);
        SubsystemWeather weather = project.FindSubsystem<SubsystemWeather>(false);
        SubsystemTime time = project.FindSubsystem<SubsystemTime>(false);

        // ---- 相机矩阵
        Matrix view = camera.ViewMatrix;
        ModelView = view.OrientationMatrix;
        ModelViewInverse = Matrix.Invert(ModelView);
        RawProjection = camera.ProjectionMatrix;
        Projection = RawProjection;
        if (Display.UseReducedZRange)
        {
            // 顶点着色器不做 z 变换时，窗口深度 = z/w * 0.5 + 0.5：把这一步并进投影矩阵，着色器里就统一按"深度 = z/w"处理
            Projection *= new Matrix(1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 0.5f, 0f, 0f, 0f, 0.5f, 1f);
        }
        ProjectionInverse = Matrix.Invert(Projection);
        CameraPosition = camera.ViewPosition;
        // 深度 d = (M33 * z + M43) / (-z)：由 d=0、d=1 反推近、远平面
        Near = Projection.M43 / Projection.M33;
        Far = Projection.M43 / (Projection.M33 + 1f);

        // ---- 太阳与时间
        SunVector = SunMath.SunDirection(timeOfDay.TimeOfDay, timeOfDay.Midday, GetSeasonAngle(sky));
        LightVector = SunVector.Y >= 0f ? SunVector : -SunVector;
        SkyLightIntensity = sky.SkyLightIntensity;
        Wetness = MathUtils.Saturate(weather?.PrecipitationIntensity ?? 0f);
        double gameTime = time?.GameTime ?? 0.0;
        FrameTimeCounter = (float)(gameTime % 3600.0);
        WorldTimeCounter = (float)(gameTime % 153600.0);
        MoonPhase = GetMoonPhase(timeOfDay);

        // Derivative shaders.properties 里的时段权重
        float meFade = SunVector.Y < 0.18f ? 0.37f + 1.2f * MathF.Max(0f, -SunVector.Y) : 1.7f;
        MeWeight = MathF.Pow(MathUtils.Saturate(1f - meFade * MathF.Abs(SunVector.Y - 0.18f)), 2f);
        TimeNoon = (SunVector.Y > 0f ? 1f : 0f) * (1f - MeWeight);
        TimeMidnight = (SunVector.Y < 0f ? 1f : 0f) * (1f - MeWeight);
        TimeSunrise = (SunVector.X > 0f ? 1f : 0f) * MeWeight;
        TimeSunset = (SunVector.X < 0f ? 1f : 0f) * MeWeight;

        float volFogTime = WorldTimeCounter * 0.01f;
        VolFogWind = new Vector3(volFogTime, 0f, volFogTime * 0.6f);
        VolFogDensity = 1f + Wetness * 2f;
        RenderDistance = MathF.Max(sky.VisibilityRange, 32f);
        EyeInWater = sky.ViewUnderWaterDepth > 0f;

        // 白天按阳光/阴影重新分配亮度，夜晚减弱
        float day = MathUtils.Saturate(SunVector.Y * 5f + 0.5f);
        LightingStrength = MathUtils.Lerp(ShaderSettings.NightLightingStrength, ShaderSettings.DayLightingStrength, day);

        UpdateShadowMatrices();
    }

    /// <summary>眼睛处是否露天（平滑过渡），对应 Derivative 的 eyeSkylightFix</summary>
    public void UpdateEyeSkylight(float target, float dt)
    {
        EyeSkylightFix = MathUtils.Lerp(EyeSkylightFix, target, MathUtils.Saturate(dt * 1.5f));
    }

    internal void UpdateShadowMatrices()
    {
        // 阴影中心按 2 格对齐，避免相机移动时阴影边缘闪烁
        const float interval = 2f;
        ShadowCenter = new Vector3(
            MathF.Floor(CameraPosition.X / interval) * interval,
            MathF.Floor(CameraPosition.Y / interval) * interval,
            MathF.Floor(CameraPosition.Z / interval) * interval);
        Matrix rotation = Matrix.CreateLookAt(LightVector, Vector3.Zero, Vector3.UnitZ);
        ShadowModelView = Matrix.CreateTranslation(CameraPosition - ShadowCenter) * rotation;
        ShadowProjection = Matrix.CreateScale(1f / ShaderSettings.ShadowDistance, 1f / ShaderSettings.ShadowDistance, -1f / ShaderSettings.ShadowDepthRange);
        ShadowProjectionInverse = Matrix.Invert(ShadowProjection);
    }

    /// <summary>引擎顶点坐标（减去整数原点 origin 后）→ 阴影裁剪空间（畸变前）</summary>
    public Matrix ShadowMatrixForOrigin(Vector3 origin) =>
        Matrix.CreateTranslation(origin - CameraPosition) * ShadowModelView * ShadowProjection;

    static float GetMoonPhase(SubsystemTimeOfDay timeOfDay)
    {
        // 与 Derivative 一致：0 = 满月，4 = 新月；生存战争按天数循环
        double day = timeOfDay.Day;
        return (float)(((int)Math.Floor(day) % 8 + 8) % 8);
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
            s_seasonAngleUnavailable = true;
            return 0f;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static float SeasonAngleCore(SubsystemSky sky) => sky.CalculateSeasonAngle();

    /// <summary>把所有公共 uniform 写入程序（程序里没有的名字自动跳过）</summary>
    public void Apply(GlProgram p)
    {
        p.Set("gbufferModelView", ModelView);
        p.Set("gbufferModelViewInverse", ModelViewInverse);
        p.Set("gbufferProjection", Projection);
        p.Set("gbufferProjectionInverse", ProjectionInverse);
        p.Set("shadowModelView", ShadowModelView);
        p.Set("shadowProjection", ShadowProjection);
        p.Set("shadowProjectionInverse", ShadowProjectionInverse);
        p.Set("cameraPosition", CameraPosition.X, CameraPosition.Y, CameraPosition.Z);
        p.Set("worldSunVector", SunVector.X, SunVector.Y, SunVector.Z);
        p.Set("worldLightVector", LightVector.X, LightVector.Y, LightVector.Z);
        p.Set("eyeAltitude", CameraPosition.Y);
        p.Set("wetness", Wetness);
        p.Set("frameTimeCounter", FrameTimeCounter);
        p.Set("worldTimeCounter", WorldTimeCounter);
        p.Set("moonPhase", MoonPhase);
        p.Set("nightVision", 0f);
        p.Set("isLightningFlashing", 0f);
        p.Set("near", Near);
        p.Set("far", Far);
        p.Set("timeNoon", TimeNoon);
        p.Set("timeMidnight", TimeMidnight);
        p.Set("timeSunrise", TimeSunrise);
        p.Set("timeSunset", TimeSunset);
        p.Set("meWeight", MeWeight);
        p.Set("eyeSkylightFix", EyeSkylightFix);
        p.Set("volFogDensity", VolFogDensity);
        p.Set("volFogWind", VolFogWind.X, VolFogWind.Y, VolFogWind.Z);
        p.Set("renderDistance", RenderDistance);
        p.Set("shadowDistance", ShaderSettings.ShadowDistance);
        p.Set("shadowMapRes", ShaderSettings.ShadowMapResolution);
        p.Set("screenSize", Width, Height);
        p.Set("screenPixelSize", 1f / Width, 1f / Height);
        p.SetInt("isEyeInWater", EyeInWater ? 1 : 0);
        p.SetInt("frameCounter", FrameCounter);
        p.Set("vanillaGain", ShaderSettings.VanillaGain);
        p.Set("lightingStrength", LightingStrength);
        p.Set("shadowLift", ShaderSettings.ShadowLift);
        p.Set("exposureBias", ShaderSettings.ExposureBias);
        p.Set("exposureSceneScale", ShaderSettings.ExposureSceneScale);
        p.Set("bloomStrength", ShaderSettings.BloomStrength);
    }
}
