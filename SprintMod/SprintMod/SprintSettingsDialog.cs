using Engine;
using Game;

namespace SprintMod;

/// <summary>
/// 设置面板：两个滑块分别调节疾跑倍率和挖掘倍率。按 Esc、再按面板键或点"完成"关闭，关闭时保存设置。
/// 面板打开期间游戏暂停（见 SprintModLoader.ChangeGameTimeDelta）。
/// 控件全部用代码创建，写法参照地图模组 TravelMapSettingsWidget。
/// </summary>
internal sealed class SprintSettingsDialog : Dialog
{
    static readonly Color Background = new(0x1B, 0x26, 0x28, 0xF4);
    static readonly Color Accent = new(0x6F, 0x8A, 0x3B);
    static readonly Color TextColor = new(0xE8, 0xEC, 0xE7);
    static readonly Color HintColor = new(0xA8, 0xB0, 0xA8);

    readonly SliderWidget m_sprintSlider;
    readonly SliderWidget m_digSlider;
    readonly BevelledButtonWidget m_resetButton;
    readonly BevelledButtonWidget m_doneButton;
    bool m_closed;

    public SprintSettingsDialog()
    {
        Size = new Vector2(440f, 300f);
        HorizontalAlignment = WidgetAlignment.Center;
        VerticalAlignment = WidgetAlignment.Center;

        Children.Add(new RectangleWidget { Size = Size, FillColor = Background, OutlineColor = Accent, OutlineThickness = 2f });

        LabelWidget title = new()
        {
            Text = "疾跑 / 挖掘 设置",
            Color = TextColor,
            FontScale = 1.1f,
            Size = new Vector2(400f, 40f),
            TextAnchor = Engine.Graphics.TextAnchor.HorizontalCenter | Engine.Graphics.TextAnchor.VerticalCenter,
        };
        Children.Add(title);
        SetWidgetPosition(title, new Vector2(20f, 12f));

        m_sprintSlider = CreateSlider(SprintModLoader.SprintMultiplier);
        m_digSlider = CreateSlider(SprintModLoader.DigMultiplier);
        AddSliderRow("疾跑速度", m_sprintSlider, 66f);
        AddSliderRow("挖掘速度", m_digSlider, 114f);

        AddHint("1.0 倍 = 该项不生效", 166f);
        AddHint($"按 {SprintModLoader.GetKeyDisplayName(SprintModLoader.ToggleKeyName, "X")} 开关全部功能（面板打开时游戏暂停）", 192f);

        m_resetButton = new BevelledButtonWidget { Text = "恢复默认", Size = new Vector2(160f, 44f), Color = TextColor, CenterColor = Background };
        Children.Add(m_resetButton);
        SetWidgetPosition(m_resetButton, new Vector2(20f, 238f));

        m_doneButton = new BevelledButtonWidget { Text = "完成", Size = new Vector2(120f, 44f), Color = TextColor, CenterColor = Accent };
        Children.Add(m_doneButton);
        SetWidgetPosition(m_doneButton, new Vector2(300f, 238f));
    }

    public override void Update()
    {
        if (m_closed)
        {
            return;
        }
        try
        {
            // 每帧同步滑块数值，拖动时立即生效
            SprintModLoader.SprintMultiplier = SprintLogic.ClampMultiplier(m_sprintSlider.Value);
            SprintModLoader.DigMultiplier = SprintLogic.ClampMultiplier(m_digSlider.Value);
            m_sprintSlider.Text = FormatSliderText(SprintModLoader.SprintMultiplier);
            m_digSlider.Text = FormatSliderText(SprintModLoader.DigMultiplier);

            if (m_resetButton.IsClicked)
            {
                m_sprintSlider.Value = SprintLogic.MinMultiplier;
                m_digSlider.Value = SprintLogic.MinMultiplier;
                return;
            }

            if (m_doneButton.IsClicked
                || Input.Cancel
                || Input.IsKeyOrMouseDownOnce(SprintModLoader.PanelKeyName))
            {
                Close();
            }
        }
        catch (Exception e)
        {
            SprintModLoader.LogErrorOnce("SprintSettingsDialog.Update", e);
            Close();
        }
    }

    void Close()
    {
        if (m_closed)
        {
            return;
        }
        m_closed = true;
        try
        {
            SettingsManager.SaveSettings();
        }
        catch (Exception e)
        {
            SprintModLoader.LogErrorOnce("SaveSettings", e);
        }
        DialogsManager.HideDialog(this);
        // 清掉本帧输入：Esc 不会再打开暂停菜单，面板键也不会立刻把面板重新打开
        Input.Clear();
    }

    static string FormatSliderText(float multiplier) => SprintLogic.FormatMultiplier(multiplier) + "x";

    static SliderWidget CreateSlider(float value) => new()
    {
        MinValue = SprintLogic.MinMultiplier,
        MaxValue = SprintLogic.MaxMultiplier,
        Granularity = SprintLogic.Step,
        Value = value,
        LayoutDirection = LayoutDirection.Horizontal,
        Size = new Vector2(260f, 34f),
        IsLabelVisible = true,
        LabelWidth = 60f,
        TextColor = TextColor,
        Text = FormatSliderText(value),
    };

    void AddSliderRow(string labelText, SliderWidget slider, float y)
    {
        LabelWidget label = new()
        {
            Text = labelText,
            Color = TextColor,
            Size = new Vector2(120f, 34f),
            TextAnchor = Engine.Graphics.TextAnchor.VerticalCenter,
        };
        Children.Add(label);
        SetWidgetPosition(label, new Vector2(30f, y));
        Children.Add(slider);
        SetWidgetPosition(slider, new Vector2(150f, y));
    }

    void AddHint(string text, float y)
    {
        LabelWidget hint = new()
        {
            Text = text,
            Color = HintColor,
            FontScale = 0.75f,
            Size = new Vector2(400f, 24f),
            TextAnchor = Engine.Graphics.TextAnchor.HorizontalCenter | Engine.Graphics.TextAnchor.VerticalCenter,
        };
        Children.Add(hint);
        SetWidgetPosition(hint, new Vector2(20f, y));
    }
}
