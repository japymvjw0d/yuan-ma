using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Engine;
using Engine.Input;
using Game;
using GameEntitySystem;

namespace SprintMod;

/// <summary>
/// 疾跑 / 加速挖掘模组。
/// Z：打开设置面板（打开期间游戏暂停），用滑块设置疾跑倍率和挖掘倍率（1.0 倍 = 不生效）。
/// X：全部功能的总开关。
/// 两个按键都注册为模组键位，可在 设置-控制-键盘键位 中修改。
/// </summary>
public sealed class SprintModLoader : ModLoader
{
    /// <summary>总开关按键（键位名沿用 v1.0，保证玩家改过的键位继续有效）</summary>
    public const string ToggleKeyName = "SprintMod_Toggle";

    /// <summary>设置面板按键（键位名沿用 v1.0）</summary>
    public const string PanelKeyName = "SprintMod_Cycle";

    const string SettingsElementName = "Sprint";
    const string SprintAttribute = "SprintMultiplier";
    const string DigAttribute = "DigMultiplier";
    const string LegacySprintAttribute = "Multiplier"; // v1.0 的旧格式

    /// <summary>总开关，每次进入存档默认关闭</summary>
    public static bool MasterEnabled { get; internal set; }

    /// <summary>疾跑倍率（1.0 = 不生效），保存到 ModSettings.xml</summary>
    public static float SprintMultiplier { get; internal set; } = SprintLogic.MinMultiplier;

    /// <summary>挖掘倍率（1.0 = 不生效），保存到 ModSettings.xml</summary>
    public static float DigMultiplier { get; internal set; } = SprintLogic.MinMultiplier;

    /// <summary>最近一次打开的设置面板；它还在 DialogsManager.Dialogs 里就表示面板开着</summary>
    static SprintSettingsDialog s_panel;

    /// <summary>每个玩家移动组件的基础速度记录，换存档后自动失效</summary>
    static readonly ConditionalWeakTable<ComponentLocomotion, SpeedState> s_speedStates = new();

    static bool s_errorLogged;

    public static bool IsPanelOpen => s_panel != null && DialogsManager.Dialogs.Contains(s_panel);

    public override void __ModInitialize()
    {
        ModsManager.RegisterHook("UpdateInput", this);
        ModsManager.RegisterHook("OnMinerDig", this);
        ModsManager.RegisterHook("ChangeGameTimeDelta", this);
        ModsManager.RegisterHook("OnProjectLoaded", this);
    }

    public override IEnumerable<KeyValuePair<string, object>> GetKeyboardMappings()
    {
        yield return new KeyValuePair<string, object>(ToggleKeyName, Key.X);
        yield return new KeyValuePair<string, object>(PanelKeyName, Key.Z);
    }

    public override void OnProjectLoaded(Project project)
    {
        MasterEnabled = false;
        s_panel = null;
    }

    /// <summary>每帧由 ComponentInput 调用，每个本地玩家各调用一次</summary>
    public override void UpdateInput(ComponentInput componentInput, WidgetInput widgetInput)
    {
        try
        {
            ComponentPlayer player = componentInput.m_componentPlayer;
            ComponentLocomotion locomotion = player?.ComponentLocomotion;
            if (locomotion == null)
            {
                return;
            }

            bool togglePressed = widgetInput.IsKeyOrMouseDownOnce(ToggleKeyName);
            bool panelPressed = widgetInput.IsKeyOrMouseDownOnce(PanelKeyName);
            if ((togglePressed || panelPressed) && !IsUiBlockingInput(player))
            {
                if (panelPressed)
                {
                    OpenPanel(player, widgetInput);
                }
                else
                {
                    MasterEnabled = !MasterEnabled;
                    string message = SprintLogic.FormatToggleMessage(
                        MasterEnabled,
                        SprintMultiplier,
                        DigMultiplier,
                        GetKeyDisplayName(PanelKeyName, "Z"));
                    player.ComponentGui?.DisplaySmallMessage(message, Color.White, false, false);
                }
            }

            SpeedState state = s_speedStates.GetValue(locomotion, l => new SpeedState(l));
            SprintLogic.ApplySpeed(locomotion, state, MasterEnabled ? SprintMultiplier : SprintLogic.MinMultiplier);
        }
        catch (Exception e)
        {
            LogErrorOnce("UpdateInput", e);
        }
    }

    /// <summary>挖掘进度每帧由 已挖时间 ÷ 挖掘时间 重新计算，乘以倍率即为按倍率加速</summary>
    public override void OnMinerDig(ComponentMiner miner, TerrainRaycastResult raycastResult, ref float digProgress, out bool digged)
    {
        digged = false;
        try
        {
            if (miner?.ComponentPlayer != null)
            {
                digProgress = SprintLogic.ScaleDigProgress(digProgress, MasterEnabled, DigMultiplier);
            }
        }
        catch (Exception e)
        {
            LogErrorOnce("OnMinerDig", e);
        }
    }

    /// <summary>设置面板打开期间让游戏时间停止，效果同游戏自带的暂停菜单</summary>
    public override void ChangeGameTimeDelta(SubsystemTime subsystemTime, ref float dt)
    {
        try
        {
            if (IsPanelOpen)
            {
                dt = 0f;
            }
        }
        catch (Exception e)
        {
            LogErrorOnce("ChangeGameTimeDelta", e);
        }
    }

    static void OpenPanel(ComponentPlayer player, WidgetInput widgetInput)
    {
        SprintSettingsDialog dialog = new();
        s_panel = dialog;
        DialogsManager.ShowDialog(player.GuiWidget, dialog);
        // 清掉本帧输入，避免同一次按键再被别处处理
        widgetInput.Clear();
    }

    /// <summary>背包等面板、任意对话框（包括本模组的设置面板）或文本框获得焦点时，不响应按键</summary>
    static bool IsUiBlockingInput(ComponentPlayer player) =>
        player.ComponentGui?.ModalPanelWidget != null
        || DialogsManager.HasDialogs(player.GuiWidget)
        || DialogsManager.HasDialogs(null)
        || HasFocusedTextBox(player.GuiWidget);

    static bool HasFocusedTextBox(Widget widget)
    {
        if (widget is TextBoxWidget { HasFocus: true })
        {
            return true;
        }
        if (widget is ContainerWidget container)
        {
            foreach (Widget child in container.Children)
            {
                if (HasFocusedTextBox(child))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>取当前绑定的按键名称（玩家可能改过键），失败时用默认值</summary>
    internal static string GetKeyDisplayName(string mappingName, string fallback)
    {
        try
        {
            object key = SettingsManager.GetKeyboardMapping(mappingName);
            string name = key?.ToString();
            return string.IsNullOrEmpty(name) || name == "Null" ? fallback : name;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    public override void SaveSettings(XElement xElement)
    {
        xElement.Add(
            new XElement(
                SettingsElementName,
                new XAttribute(SprintAttribute, SprintLogic.FormatMultiplier(SprintMultiplier)),
                new XAttribute(DigAttribute, SprintLogic.FormatMultiplier(DigMultiplier))
            )
        );
    }

    public override void LoadSettings(XElement xElement)
    {
        XElement element = xElement?.Element(SettingsElementName);
        if (element == null)
        {
            return;
        }
        string sprint = element.Attribute(SprintAttribute)?.Value ?? element.Attribute(LegacySprintAttribute)?.Value;
        if (SprintLogic.TryParseMultiplier(sprint, out float sprintMultiplier))
        {
            SprintMultiplier = sprintMultiplier;
        }
        if (SprintLogic.TryParseMultiplier(element.Attribute(DigAttribute)?.Value, out float digMultiplier))
        {
            DigMultiplier = digMultiplier;
        }
    }

    internal static void LogErrorOnce(string where, Exception e)
    {
        if (!s_errorLogged)
        {
            s_errorLogged = true;
            Log.Error($"[SprintMod] {where} failed: {e}");
        }
    }
}

/// <summary>记录某个移动组件的基础速度和本模组最后一次写入的速度</summary>
internal sealed class SpeedState
{
    public float BaseWalkSpeed;
    public float BaseFlySpeed;
    public float AppliedWalkSpeed;
    public float AppliedFlySpeed;

    public SpeedState(ComponentLocomotion locomotion)
    {
        BaseWalkSpeed = AppliedWalkSpeed = locomotion.WalkSpeed;
        BaseFlySpeed = AppliedFlySpeed = locomotion.CreativeFlySpeed;
    }
}

/// <summary>不依赖游戏界面的纯逻辑，便于测试</summary>
internal static class SprintLogic
{
    public const float MinMultiplier = 1f;
    public const float MaxMultiplier = 5f;
    public const float Step = 0.1f;

    /// <summary>限制到 [1, 5] 并按 0.1 取整</summary>
    public static float ClampMultiplier(float value) =>
        float.IsFinite(value)
            ? MathF.Round(Math.Clamp(value, MinMultiplier, MaxMultiplier) / Step) * Step
            : MinMultiplier;

    public static bool TryParseMultiplier(string text, out float multiplier)
    {
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && float.IsFinite(value))
        {
            multiplier = ClampMultiplier(value);
            return true;
        }
        multiplier = MinMultiplier;
        return false;
    }

    /// <summary>倍率大于 1.0 才算生效</summary>
    public static bool IsActive(float multiplier) => multiplier > MinMultiplier + 0.001f;

    public static string FormatMultiplier(float multiplier) => multiplier.ToString("0.0", CultureInfo.InvariantCulture);

    public static string FormatToggleMessage(bool enabled, float sprintMultiplier, float digMultiplier, string panelKey)
    {
        if (!enabled)
        {
            return "功能：关闭";
        }
        List<string> parts = [];
        if (IsActive(sprintMultiplier))
        {
            parts.Add($"疾跑 {FormatMultiplier(sprintMultiplier)} 倍");
        }
        if (IsActive(digMultiplier))
        {
            parts.Add($"挖掘 {FormatMultiplier(digMultiplier)} 倍");
        }
        return parts.Count == 0
            ? $"功能：开启（未设置参数，按 {panelKey} 设置）"
            : $"功能：开启（{string.Join("，", parts)}）";
    }

    public static float ScaleDigProgress(float progress, bool enabled, float digMultiplier) =>
        enabled && IsActive(digMultiplier) ? Math.Clamp(progress * digMultiplier, 0f, 1f) : progress;

    /// <summary>
    /// 把速度设为 基础速度 × 倍率。如果速度被游戏或其他模组改过，就把新值当作基础速度，
    /// 这样关闭时能准确恢复原速
    /// </summary>
    public static void ApplySpeed(ComponentLocomotion locomotion, SpeedState state, float multiplier)
    {
        if (locomotion.WalkSpeed != state.AppliedWalkSpeed)
        {
            state.BaseWalkSpeed = locomotion.WalkSpeed;
        }
        if (locomotion.CreativeFlySpeed != state.AppliedFlySpeed)
        {
            state.BaseFlySpeed = locomotion.CreativeFlySpeed;
        }
        locomotion.WalkSpeed = state.AppliedWalkSpeed = state.BaseWalkSpeed * multiplier;
        locomotion.CreativeFlySpeed = state.AppliedFlySpeed = state.BaseFlySpeed * multiplier;
    }
}
