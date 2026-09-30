namespace ShaderMod.Pipeline;

/// <summary>
/// 光影质量与调色参数（默认"高"，面向中高端独显）。想调效果改这里，重新生成即可。
/// </summary>
internal static class ShaderSettings
{
    // ---- 质量
    /// <summary>阴影贴图边长（像素）</summary>
    public const int ShadowMapResolution = 2048;
    /// <summary>阴影覆盖半径（格）</summary>
    public const float ShadowDistance = 128f;
    /// <summary>阴影空间深度范围（格，Derivative 的深度再压缩 5 倍后约 ±640 格）</summary>
    public const float ShadowDepthRange = 128f;
    /// <summary>PCF 软阴影采样数</summary>
    public const int PcfSamples = 12;
    /// <summary>体积云步进次数</summary>
    public const int CloudSamples = 32;
    /// <summary>云穹图大小；每帧更新 1/CloudDomeBands，数帧刷新一遍（云移动很慢，看不出来）</summary>
    public const int CloudDomeWidth = 2048, CloudDomeHeight = 512, CloudDomeBands = 8;
    /// <summary>体积光（丁达尔）最大步进次数（半分辨率）</summary>
    public const int VolumetricFogSamples = 12;
    /// <summary>水面反射（屏幕空间光线追踪）步数</summary>
    public const int ReflectionSamples = 16;
    /// <summary>高度图边长（格）：用于判断露天 / 头顶有遮挡</summary>
    public const int HeightMapSize = 512;
    /// <summary>高度图每帧更新的行数</summary>
    public const int HeightMapRowsPerFrame = 64;

    // ---- 画面
    /// <summary>原版颜色换算到 HDR 的增益（越大地形越亮）</summary>
    public const float VanillaGain = 1.0f;
    /// <summary>白天阳光 / 阴影重新分配亮度的强度（0 = 保持原版明暗）</summary>
    public const float DayLightingStrength = 1.0f;
    /// <summary>夜晚的强度（月光阴影较弱，避免把火把照亮的地方压暗）</summary>
    public const float NightLightingStrength = 0.3f;
    /// <summary>阴影里保留的最低亮度比例</summary>
    public const float ShadowLift = 0.35f;
    /// <summary>曝光补偿（EV，正数更亮）</summary>
    public const float ExposureBias = 0.0f;
    /// <summary>光照强度 → 场景平均亮度的换算系数（越大整体越暗）</summary>
    public const float ExposureSceneScale = 1.0f;
    /// <summary>泛光强度倍数</summary>
    public const float BloomStrength = 1.0f;
}
