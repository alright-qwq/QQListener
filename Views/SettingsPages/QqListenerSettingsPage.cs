using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Controls;
using ClassIsland.Core.Assists;
using FluentAvalonia.UI.Controls;
using ClassIsland.Core.Attributes;
using QQListener.Models;

namespace QQListener.Views.SettingsPages;

[SettingsPageInfo("qqlistener.settings", "QQListener", "\uE713", "\uE715")]
public class QqListenerSettingsPage : SettingsPageBase
{
    private readonly QqListenerSettings _settings;

    public QqListenerSettingsPage(QqListenerSettings settings)
    {
        _settings = settings;
        Content = BuildContent();
    }

    private Control BuildContent()
    {
        var panel = new StackPanel
        {
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        panel.Classes.Add("settings-container");
        panel.Classes.Add("animated-intro");

        panel.Children.Add(CreateCheckBox("启用 QQ 通知监听", _settings.IsEnabled, value => _settings.IsEnabled = value));
        panel.Children.Add(CreateCheckBox("仅处理 QQ 通知", _settings.QqOnly, value => _settings.QqOnly = value));
        panel.Children.Add(CreateCheckBox("有人 @ 我时作为重要消息", _settings.SomeoneAtMe, value => _settings.SomeoneAtMe = value));
        panel.Children.Add(CreateCheckBox("启用呼叫关键词", _settings.CallingEnabled, value => _settings.CallingEnabled = value));
        panel.Children.Add(CreateTextBox("呼叫关键词", _settings.CallingKeyword, value => _settings.CallingKeyword = value));
        panel.Children.Add(CreateDoubleBox("扫描间隔（秒）", _settings.ScanIntervalSeconds, value => _settings.ScanIntervalSeconds = value));
        panel.Children.Add(CreateIntBox("去重冷却（秒）", _settings.CooldownSeconds, value => _settings.CooldownSeconds = value));
        panel.Children.Add(CreateIntBox("普通消息显示时长（秒）", _settings.NormalDurationSeconds, value => _settings.NormalDurationSeconds = value));
        panel.Children.Add(CreateIntBox("重要消息显示时长（秒）", _settings.ImportantDurationSeconds, value => _settings.ImportantDurationSeconds = value));
        panel.Children.Add(CreateIntBox("呼叫消息显示时长（秒）", _settings.CallingDurationSeconds, value => _settings.CallingDurationSeconds = value));
        panel.Children.Add(CreateSliderField("滚动速度", _settings.RollingSpeed, 0.0, 4.0, value => _settings.RollingSpeed = value));
        panel.Children.Add(CreateTextArea("重要人物（每行一个）", _settings.ImportantPersonsText, value => _settings.ImportantPersonsText = value));
        panel.Children.Add(CreateTextArea("重要关键词（每行一个）", _settings.ImportantKeywordsText, value => _settings.ImportantKeywordsText = value));
        panel.Children.Add(CreateTextArea("黑名单（每行一个）", _settings.BlacklistText, value => _settings.BlacklistText = value));

        return new ScrollViewer
        {
            Content = panel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
    }

    private Control CreateCheckBox(string text, bool value, Action<bool> apply)
    {
        var toggle = new ToggleSwitch
        {
            IsChecked = value,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        toggle.IsCheckedChanged += (_, _) =>
        {
            apply(toggle.IsChecked == true);
            SaveSettings();
        };
        return Wrap(text, toggle);
    }

    private Control CreateTextBox(string label, string value, Action<string> apply)
    {
        var textBox = new TextBox
        {
            Text = value,
            Watermark = label,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        textBox.LostFocus += (_, _) =>
        {
            apply(textBox.Text ?? "");
            SaveSettings();
        };
        return Wrap(label, textBox);
    }

    private Control CreateTextArea(string label, string value, Action<string> apply)
    {
        var textBox = new TextBox
        {
            Text = value,
            Watermark = label,
            AcceptsReturn = true,
            MinHeight = 120,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        textBox.LostFocus += (_, _) =>
        {
            apply(textBox.Text ?? "");
            SaveSettings();
        };
        return Wrap(label, textBox);
    }

    private Control CreateDoubleBox(string label, double value, Action<double> apply)
    {
        var textBox = new TextBox
        {
            Text = value.ToString("0.###"),
            Watermark = label,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        textBox.LostFocus += (_, _) =>
        {
            if (double.TryParse(textBox.Text, out var parsed))
            {
                apply(parsed);
                SaveSettings();
            }
        };
        return Wrap(label, textBox);
    }

    private Control CreateIntBox(string label, int value, Action<int> apply)
    {
        var textBox = new TextBox
        {
            Text = value.ToString(),
            Watermark = label,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        textBox.LostFocus += (_, _) =>
        {
            if (int.TryParse(textBox.Text, out var parsed))
            {
                apply(parsed);
                SaveSettings();
            }
        };
        return Wrap(label, textBox);
    }

    private Control CreateSlider(string label, double value, double min, double max, Action<double> apply)
    {
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            TickFrequency = 0.1,
            Width = 300
        };

        // Floating value popup above thumb while dragging
        var popupText = new TextBlock
        {
            Text = slider.Value.ToString("0.##"),
            Foreground = Brushes.White,
            FontWeight = FontWeight.Bold
        };
        var popupBorder = new Border
        {
            Background = Brushes.Black,
            Opacity = 0.9,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 4),
            Child = popupText,
            IsVisible = false
        };

        var overlay = new Canvas { Width = slider.Width, Height = 40, IsHitTestVisible = false };
        overlay.Children.Add(popupBorder);

        var container = new Grid { Width = slider.Width };
        container.Children.Add(slider);
        container.Children.Add(overlay);

        void UpdatePopup(double pointerX)
        {
            popupText.Text = slider.Value.ToString("0.##");
            // measure to get correct size
            popupBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var w = popupBorder.DesiredSize.Width;
            var h = popupBorder.DesiredSize.Height;
            var left = pointerX - w / 2;
            left = Math.Clamp(left, 0, slider.Width - w);
            Canvas.SetLeft(popupBorder, left);
            Canvas.SetTop(popupBorder, -h - 6);
        }

        slider.PointerPressed += (_, e) =>
        {
            popupBorder.IsVisible = true;
            var p = e.GetPosition(slider);
            UpdatePopup(p.X);
        };
        slider.PointerMoved += (_, e) =>
        {
            if (!popupBorder.IsVisible) return;
            var p = e.GetPosition(slider);
            UpdatePopup(p.X);
        };
        slider.PointerReleased += (_, e) =>
        {
            popupBorder.IsVisible = false;
        };
        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
            {
                apply(slider.Value);
                SaveSettings();
                if (popupBorder.IsVisible)
                {
                    // update popup position based on current thumb location
                    // try to use thumb position if possible, fallback to proportion
                    var percent = (slider.Value - slider.Minimum) / (slider.Maximum - slider.Minimum);
                    var x = percent * slider.Width;
                    UpdatePopup(x);
                }
            }
        };
        return Wrap(label, container);
    }

    private Control CreateSliderField(string label, double value, double min, double max, Action<double> apply)
    {
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            TickFrequency = 0.1,
            Width = 180,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Use project's slider auto-tooltip assist (same as official)
        SliderDragTooltipAssist.SetStringFormat(slider, "F2");
        slider.Classes.Add("auto-tooltip");

        var footerStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        footerStack.Children.Add(slider);

        var expander = new SettingsExpander
        {
            Header = label
        };
        expander.Footer = footerStack;

        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
            {
                apply(slider.Value);
                SaveSettings();
            }
        };

        return expander;
    }

    private static Control Wrap(string label, Control control)
    {
        // Place the label as the SettingsExpander header and the control inside a Field in the footer,
        // matching the host app's per-row boxed layout (label left, control right).
        var footerField = new Field
        {
            Content = control,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 44
        };

        // Keep ToggleSwitch alignment to the right; stretch others
        if (control is not ToggleSwitch)
        {
            control.HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        var expander = new SettingsExpander
        {
            Header = label
        };
        expander.Footer = footerField;
        return expander;
    }

    private void SaveSettings()
    {
        _settings.Normalize();
        _settings.Save();
    }
}
