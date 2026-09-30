using System.Xml.Linq;
using Engine;
using Engine.Input;
using Game;
using ShaderMod.Pipeline;

namespace ShaderMod;

/// <summary>
/// Derivative 光影移植（仅自用）。
/// 渲染流程接入见 Pipeline/RenderPatches.cs，着色器见 Glsl/（移植的 Derivative 库 + 各通道）。
/// 按 O（可在 设置-控制-键盘键位 中修改）开关光影，关闭时画面与原版一致。
/// </summary>
public sealed class ShaderModLoader : ModLoader
{
    public const string ToggleKeyName = "ShaderMod_Toggle";

    const string SettingsElementName = "Shader";
    const string EnabledAttribute = "Enabled";

    /// <summary>光影开关，默认开启，保存到 ModSettings.xml</summary>
    public static bool Enabled { get; internal set; } = true;

    static bool s_errorLogged;

    public override void __ModInitialize()
    {
        ModsManager.RegisterHook("UpdateInput", this);
        ModsManager.RegisterHook("OnProjectDisposed", this);
        RenderPatches.TryInstall();
    }

    public override IEnumerable<KeyValuePair<string, object>> GetKeyboardMappings()
    {
        yield return new KeyValuePair<string, object>(ToggleKeyName, Key.O);
    }

    /// <summary>每帧由 ComponentInput 调用：处理开关键</summary>
    public override void UpdateInput(ComponentInput componentInput, WidgetInput widgetInput)
    {
        try
        {
            ComponentPlayer player = componentInput.m_componentPlayer;
            if (player != null
                && widgetInput.IsKeyOrMouseDownOnce(ToggleKeyName)
                && !IsUiBlockingInput(player))
            {
                Enabled = !Enabled;
                player.ComponentGui?.DisplaySmallMessage(FormatToggleMessage(Enabled, RenderPatches.Failed), Color.White, false, false);
                SettingsManager.SaveSettings();
            }
        }
        catch (Exception e)
        {
            LogErrorOnce("UpdateInput", e);
        }
    }

    public override void OnProjectDisposed()
    {
        RenderPatches.DisposeResources();
    }

    internal static string FormatToggleMessage(bool enabled, bool failed) =>
        !enabled ? "光影：关" : failed ? "光影：开（本次运行出错已停用，详见日志）" : "光影：开";

    /// <summary>背包等面板、任意对话框或文本框获得焦点时，不响应按键</summary>
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

    public override void SaveSettings(XElement xElement)
    {
        xElement.Add(new XElement(SettingsElementName, new XAttribute(EnabledAttribute, Enabled ? "true" : "false")));
    }

    public override void LoadSettings(XElement xElement)
    {
        string value = xElement?.Element(SettingsElementName)?.Attribute(EnabledAttribute)?.Value;
        if (bool.TryParse(value, out bool enabled))
        {
            Enabled = enabled;
        }
    }

    static void LogErrorOnce(string where, Exception e)
    {
        if (!s_errorLogged)
        {
            s_errorLogged = true;
            Log.Error($"[ShaderMod] {where} failed: {e}");
        }
    }
}
