using Engine;
using Game;

namespace SprintMod;

/// <summary>
/// 设置面板：四个滑块分别调节疾跑、挖掘、攻击力、攻击距离倍率。挖掘滑块最右端一档为"秒挖"。
/// 按 Esc、再按面板键或点"完成"关闭，关闭时保存设置。
/// 面板打开期间游戏暂停（见 SprintModLoader.ChangeGameTimeDelta）。
/// 控件全部用代码创建，写法参照地图模组 TravelMapSettingsWidget。
/// </summary>
internal sealed class SprintSettingsDialog : Dialog
{
    static readonly Color Background = new(0x1B, 0x26, 0x28, 0xF4);
    static readonly Color Accent = new(0x6F, 0x8A, 0x3B);
    static readonly Color TextColor = new(0xE8, 0xEC, 0xE7);
    static readonly Color HintColor = new(0xA8, 0xB0, 0xA8);

    const float RowTop = 66f;
    const float RowHeight = 46f;

    readonly SliderWidget m_sprintSlider;
    readonly SliderWidget m_digSlider;
    readonly SliderWidget m_attackSlider;
    readonly SliderWidget m_rangeSlider;
    readonly BevelledButtonWidget m_resetButton;
    readonly BevelledButtonWidget m_doneButton;
    bool m_closed;

    public SprintSettingsDialog()
    {
        Size = new Vector2(440f, 380f);
        HorizontalAlignment = WidgetAlignment.Center;
        VerticalAlignment = WidgetAlignment.Center;

        Children.Add(new RectangleWidget { Size = Size, FillColor = Background, OutlineColor = Accent, OutlineThickness = 2f });

        LabelWidget title = new()
        {
            Text = "疾跑 / 挖掘 / 攻击 设置",
            Color = TextColor,
            FontScale = 1.1f,
            Size = new Vector2(400f, 40f),
            TextAnchor = Engine.Graphics.TextAnchor.HorizontalCenter | Engine.Graphics.TextAnchor.VerticalCenter,
        };
        Children.Add(title);
        SetWidgetPosition(title, new Vector2(20f, 12f));

        m_sprintSlider = CreateSlider(SprintModLoader.SprintMultiplier, SprintLogic.MaxMultiplier, FormatMultiplierText);
        m_digSlider = CreateSlider(SprintModLoader.DigMultiplier, SprintLogic.MaxDigMultiplier, FormatDigText);
        m_attackSlider = CreateSlider(SprintModLoader.AttackMultiplier, SprintLogic.MaxMultiplier, FormatMultiplierText);
        m_rangeSlider = CreateSlider(SprintModLoader.RangeMultiplier, SprintLogic.MaxMultiplier, FormatMultiplierText);
        AddSliderRow("疾跑速度", m_sprintSlider, RowTop);
        AddSliderRow("挖掘速度", m_digSlider, RowTop + RowHeight);
        AddSliderRow("攻击力", m_attackSlider, RowTop + 2 * RowHeight);
        AddSliderRow("攻击距离", m_rangeSlider, RowTop + 3 * RowHeight);

        float hintTop = RowTop + 4 * RowHeight + 8f;
        AddHint("1.0 倍 = 该项不生效；挖掘拖到最右端 = 秒挖", hintTop);
        AddHint($"按 {SprintModLoader.GetKeyDisplayName(SprintModLoader.ToggleKeyName, "X")} 开关全部功能（面板打开时游戏暂停）", hintTop + 26f);

        float buttonTop = Size.Y - 58f;
        m_resetButton = new BevelledButtonWidget { Text = "恢复默认", Size = new Vector2(160f, 44f), Color = TextColor, CenterColor = Background };
        Children.Add(m_resetButton);
        SetWidgetPosition(m_resetButton, new Vector2(20f, buttonTop));

        m_doneButton = new BevelledButtonWidget { Text = "完成", Size = new Vector2(120f, 44f), Color = TextColor, CenterColor = Accent };
        Children.Add(m_doneButton);
        SetWidgetPosition(m_doneButton, new Vector2(300f, buttonTop));
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
            SprintModLoader.DigMultiplier = SprintLogic.ClampDigMultiplier(m_digSlider.Value);
            SprintModLoader.AttackMultiplier = SprintLogic.ClampMultiplier(m_attackSlider.Value);
            SprintModLoader.RangeMultiplier = SprintLogic.ClampMultiplier(m_rangeSlider.Value);
            m_sprintSlider.Text = FormatMultiplierText(SprintModLoader.SprintMultiplier);
            m_digSlider.Text = FormatDigText(SprintModLoader.DigMultiplier);
            m_attackSlider.Text = FormatMultiplierText(SprintModLoader.AttackMultiplier);
            m_rangeSlider.Text = FormatMultiplierText(SprintModLoader.RangeMultiplier);

            if (m_resetButton.IsClicked)
            {
                m_sprintSlider.Value = SprintLogic.MinMultiplier;
                m_digSlider.Value = SprintLogic.MinMultiplier;
                m_attackSlider.Value = SprintLogic.MinMultiplier;
                m_rangeSlider.Value = SprintLogic.MinMultiplier;
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

    static string FormatMultiplierText(float multiplier) => SprintLogic.FormatMultiplier(multiplier) + "x";

    static string FormatDigText(float multiplier) =>
        SprintLogic.IsInstantDig(multiplier) ? "秒挖" : FormatMultiplierText(multiplier);

    static SliderWidget CreateSlider(float value, float maxValue, Func<float, string> format) => new()
    {
        MinValue = SprintLogic.MinMultiplier,
        MaxValue = maxValue,
        Granularity = SprintLogic.Step,
        Value = value,
        LayoutDirection = LayoutDirection.Horizontal,
        Size = new Vector2(260f, 34f),
        IsLabelVisible = true,
        LabelWidth = 60f,
        TextColor = TextColor,
        Text = format(value),
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
