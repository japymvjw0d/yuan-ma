using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Engine;
using Engine.Input;
using Game;

namespace SprintMod;

/// <summary>
/// 疾跑模组：按 X 开关疾跑，按 Z 循环切换倍率 1.5 → 2 → 3 倍。
/// 倍率作用于行走速度（WalkSpeed）和创造飞行速度（CreativeFlySpeed），只在内存中修改，不写入存档。
/// 两个按键都注册为模组键位，可在 设置-控制-键盘键位 中修改。
/// </summary>
public sealed class SprintModLoader : ModLoader
{
    public const string ToggleKeyName = "SprintMod_Toggle";
    public const string CycleKeyName = "SprintMod_Cycle";

    /// <summary>倍率档位，最高 3 倍，防止地形加载跟不上或卡进方块</summary>
    public static readonly float[] Multipliers = [1.5f, 2f, 3f];

    const string SettingsElementName = "Sprint";
    const string SettingsMultiplierAttribute = "Multiplier";

    /// <summary>当前倍率档位（保存到 ModSettings.xml）</summary>
    public static int MultiplierIndex { get; private set; }

    public static float CurrentMultiplier => Multipliers[MultiplierIndex];

    /// <summary>每个玩家的疾跑状态。键是玩家的移动组件，换存档后自动失效，因此疾跑默认回到"关"</summary>
    static readonly ConditionalWeakTable<ComponentLocomotion, SprintState> s_states = new();

    static bool s_errorLogged;

    sealed class SprintState
    {
        public bool Enabled;
        public float BaseWalkSpeed;
        public float BaseFlySpeed;
        public float AppliedWalkSpeed;
        public float AppliedFlySpeed;

        public SprintState(ComponentLocomotion locomotion)
        {
            BaseWalkSpeed = AppliedWalkSpeed = locomotion.WalkSpeed;
            BaseFlySpeed = AppliedFlySpeed = locomotion.CreativeFlySpeed;
        }
    }

    public override void __ModInitialize()
    {
        ModsManager.RegisterHook("UpdateInput", this);
    }

    public override IEnumerable<KeyValuePair<string, object>> GetKeyboardMappings()
    {
        yield return new KeyValuePair<string, object>(ToggleKeyName, Key.X);
        yield return new KeyValuePair<string, object>(CycleKeyName, Key.Z);
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
            SprintState state = s_states.GetValue(locomotion, l => new SprintState(l));

            bool togglePressed = widgetInput.IsKeyOrMouseDownOnce(ToggleKeyName);
            bool cyclePressed = widgetInput.IsKeyOrMouseDownOnce(CycleKeyName);
            if ((togglePressed || cyclePressed) && !IsUiBlockingInput(player))
            {
                if (togglePressed)
                {
                    state.Enabled = !state.Enabled;
                }
                if (cyclePressed)
                {
                    MultiplierIndex = (MultiplierIndex + 1) % Multipliers.Length;
                    // 立即写盘，避免游戏异常退出时丢失档位
                    SettingsManager.SaveSettings();
                }
                player.ComponentGui?.DisplaySmallMessage(FormatMessage(state.Enabled), Color.White, false, false);
            }

            ApplySpeed(locomotion, state);
        }
        catch (Exception e)
        {
            if (!s_errorLogged)
            {
                s_errorLogged = true;
                Log.Error($"[SprintMod] UpdateInput failed: {e}");
            }
        }
    }

    /// <summary>背包等面板、任意对话框（命名牌、告示牌、暂停菜单等）或文本框获得焦点时，不响应疾跑按键</summary>
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

    /// <summary>
    /// 把速度设为 基础速度 × 倍率。如果速度被游戏或其他模组改过，就把新值当作基础速度，
    /// 这样关闭疾跑时能准确恢复原速
    /// </summary>
    static void ApplySpeed(ComponentLocomotion locomotion, SprintState state)
    {
        if (locomotion.WalkSpeed != state.AppliedWalkSpeed)
        {
            state.BaseWalkSpeed = locomotion.WalkSpeed;
        }
        if (locomotion.CreativeFlySpeed != state.AppliedFlySpeed)
        {
            state.BaseFlySpeed = locomotion.CreativeFlySpeed;
        }
        float multiplier = state.Enabled ? CurrentMultiplier : 1f;
        locomotion.WalkSpeed = state.AppliedWalkSpeed = state.BaseWalkSpeed * multiplier;
        locomotion.CreativeFlySpeed = state.AppliedFlySpeed = state.BaseFlySpeed * multiplier;
    }

    public static string FormatMessage(bool enabled) =>
        $"疾跑：{(enabled ? "开" : "关")}（{CurrentMultiplier.ToString("0.#", CultureInfo.InvariantCulture)} 倍）";

    public override void SaveSettings(XElement xElement)
    {
        xElement.Add(
            new XElement(
                SettingsElementName,
                new XAttribute(SettingsMultiplierAttribute, CurrentMultiplier.ToString(CultureInfo.InvariantCulture))
            )
        );
    }

    public override void LoadSettings(XElement xElement)
    {
        string value = xElement?.Element(SettingsElementName)?.Attribute(SettingsMultiplierAttribute)?.Value;
        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float multiplier))
        {
            int index = Array.IndexOf(Multipliers, multiplier);
            if (index >= 0)
            {
                MultiplierIndex = index;
            }
        }
    }
}
