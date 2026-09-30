using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using Engine.Graphics;
using Game;
using HarmonyLib;

namespace ShaderMod.Pipeline;

/// <summary>
/// 用游戏自带的 HarmonyX 接入渲染流程（都是薄包装：真正的逻辑在 NoInlining 方法里，并用 try/catch 包住）。
/// 任何一步出错：记一次日志、本局关闭光影管线，画面退回原版；按 O 仍可开关（关闭时与原版完全一致）。
/// </summary>
internal static class RenderPatches
{
    static FieldInfo s_scalingTargetField;
    static bool s_failed;
    static bool s_disposePending;

    public static bool Installed { get; private set; }
    public static bool Failed => s_failed;

    static bool IsActive => Installed && !s_failed && ShaderModLoader.Enabled;

    public static void TryInstall()
    {
        try
        {
            InstallCore();
        }
        catch (Exception e)
        {
            Log.Warning($"[ShaderMod] Shader pipeline disabled, failed to patch the game: {e}");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void InstallCore()
    {
        MethodInfo setup = AccessTools.Method(typeof(ViewWidget), "SetupScalingRenderTarget");
        MethodInfo apply = AccessTools.Method(typeof(ViewWidget), "ApplyScalingRenderTarget");
        MethodInfo skyDraw = AccessTools.Method(typeof(SubsystemSky), "Draw", [typeof(Camera), typeof(int)]);
        s_scalingTargetField = AccessTools.Field(typeof(ViewWidget), "m_scalingRenderTarget");
        if (setup == null || apply == null || skyDraw == null || s_scalingTargetField == null)
        {
            Log.Warning("[ShaderMod] Shader pipeline disabled: required game members not found in this game version.");
            return;
        }
        Harmony harmony = new("ShaderMod.Pipeline");
        harmony.Patch(setup, prefix: new HarmonyMethod(typeof(RenderPatches), nameof(SetupPrefix)));
        harmony.Patch(apply,
            prefix: new HarmonyMethod(typeof(RenderPatches), nameof(ApplyPrefix)),
            postfix: new HarmonyMethod(typeof(RenderPatches), nameof(ApplyPostfix)));
        harmony.Patch(skyDraw, prefix: new HarmonyMethod(typeof(RenderPatches), nameof(SkyDrawPrefix)));
        Installed = true;
    }

    // ---------------- ViewWidget.SetupScalingRenderTarget

    /// <returns>false = 已由本模组设置好渲染目标，跳过原版</returns>
    public static bool SetupPrefix(ViewWidget __instance)
    {
        if (s_disposePending)
        {
            // 出错后在下一帧开始时（不在绘制中途）释放资源
            s_disposePending = false;
            DisposeResources();
        }
        if (!IsActive)
        {
            SetTerrainParametersSafe(__instance, false);
            return true;
        }
        try
        {
            bool handled = SetupCore(__instance);
            SetTerrainParametersSafe(__instance, handled);
            return !handled;
        }
        catch (Exception e)
        {
            Fail(e);
            SetTerrainParametersSafe(__instance, false);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static bool SetupCore(ViewWidget widget)
    {
        RenderTarget2D target = s_scalingTargetField.GetValue(widget) as RenderTarget2D;
        bool handled = GamePipeline.GetOrCreate().Setup(widget, ref target);
        s_scalingTargetField.SetValue(widget, target);
        return handled;
    }

    static void SetTerrainParametersSafe(ViewWidget widget, bool active)
    {
        try
        {
            SetTerrainParametersCore(widget, active);
        }
        catch (Exception e)
        {
            Fail(e);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void SetTerrainParametersCore(ViewWidget widget, bool active)
    {
        Vector4 slots = GamePipeline.Instance?.WaterSlots ?? new Vector4(-1f);
        GamePipeline.ApplyTerrainParameters(widget.GameWidget?.SubsystemGameWidgets?.Project, active, slots);
    }

    // ---------------- SubsystemSky.Draw

    /// <returns>false = 已画好 Derivative 天空，跳过原版的天空、日月星辰和云</returns>
    public static bool SkyDrawPrefix(SubsystemSky __instance, int drawOrder)
    {
        if (!IsActive || GamePipeline.Instance == null || !GamePipeline.Instance.FrameActive)
        {
            return true;
        }
        try
        {
            return !SkyDrawCore(__instance, drawOrder);
        }
        catch (Exception e)
        {
            Fail(e);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static bool SkyDrawCore(SubsystemSky sky, int drawOrder)
    {
        // 顺序 -100：原版在这里计算雾和水下状态，必须照常执行；顺序 5：天空穹顶、日月星辰、云
        if (drawOrder != sky.DrawOrders[1])
        {
            return false;
        }
        if (!sky.DrawSkyEnabled || SettingsManager.SkyRenderingMode == SkyRenderingMode.Disabled)
        {
            return false;
        }
        return GamePipeline.Instance.DrawSky(sky);
    }

    // ---------------- ViewWidget.ApplyScalingRenderTarget

    public static void ApplyPrefix(ViewWidget __instance, out RenderTarget2D __state)
    {
        __state = null;
        if (!IsActive || GamePipeline.Instance == null)
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
        if (s_scalingTargetField.GetValue(widget) is not RenderTarget2D scene
            || !ReferenceEquals(scene, GamePipeline.Instance.SceneTarget))
        {
            return null;
        }
        RenderTarget2D result = GamePipeline.Instance.Apply();
        if (result == null)
        {
            return null;
        }
        s_scalingTargetField.SetValue(widget, result);
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
            s_scalingTargetField.SetValue(__instance, __state);
        }
        catch (Exception e)
        {
            Fail(e);
        }
    }

    // ----------------

    public static void DisposeResources()
    {
        try
        {
            DisposeCore();
        }
        catch (Exception e)
        {
            Log.Warning($"[ShaderMod] Failed to release shader resources: {e}");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void DisposeCore() => GamePipeline.DisposeInstance();

    static void Fail(Exception e)
    {
        if (!s_failed)
        {
            s_failed = true;
            Log.Error($"[ShaderMod] Shader pipeline failed and has been turned off for this session: {e}");
            s_disposePending = true;
        }
    }
}
