using System.Xml.Linq;
using Engine;
using Engine.Input;
using Game;

namespace ShaderMod;

/// <summary>
/// BSL 风格光影模组。
/// 第一档：Assets/Shaders 覆盖原版地形着色器（向阳暖色/背阴冷色、提饱和、水面波光与高光）；
/// 第二档：HarmonyX 接入 ViewWidget 做全屏后处理（泛光、色调曲线、暗角），见 PostProcessor.cs。
/// 按 O（可在 设置-控制-键盘键位 中修改）开关光影，关闭时画面与原版一致。
/// </summary>
public sealed class ShaderModLoader : ModLoader
{
    public const string ToggleKeyName = "ShaderMod_Toggle";

    const string SettingsElementName = "Shader";
    const string EnabledAttribute = "Enabled";

    /// <summary>光影开关，默认开启，保存到 ModSettings.xml</summary>
    public static bool Enabled { get; internal set; } = true;

    static bool s_uniformsFailed;
    static bool s_errorLogged;

    public override void __ModInitialize()
    {
        ModsManager.RegisterHook("UpdateInput", this);
        ModsManager.RegisterHook("OnProjectDisposed", this);
        PostProcessPatches.TryInstall();
    }

    public override IEnumerable<KeyValuePair<string, object>> GetKeyboardMappings()
    {
        yield return new KeyValuePair<string, object>(ToggleKeyName, Key.O);
    }

    /// <summary>每帧由 ComponentInput 调用：处理开关键，并把光影参数写入地形着色器</summary>
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
                player.ComponentGui?.DisplaySmallMessage(FormatToggleMessage(Enabled), Color.White, false, false);
                SettingsManager.SaveSettings();
            }
        }
        catch (Exception e)
        {
            LogErrorOnce("UpdateInput", e);
        }

        if (s_uniformsFailed)
        {
            return;
        }
        try
        {
            TerrainUniforms.Update(componentInput.Project, Enabled);
        }
        catch (Exception e)
        {
            // 参数写不进去时，地形着色器的 u_shaderStrength 保持 0，即原版画面
            s_uniformsFailed = true;
            LogErrorOnce("TerrainUniforms", e);
        }
    }

    public override void OnProjectDisposed()
    {
        try
        {
            TerrainUniforms.Reset();
            PostProcessor.DisposeTargets();
        }
        catch (Exception e)
        {
            LogErrorOnce("OnProjectDisposed", e);
        }
    }

    internal static string FormatToggleMessage(bool enabled) => enabled ? "光影：开" : "光影：关";

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
