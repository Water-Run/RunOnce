/*
 * 设置页面视图
 * 提供应用程序配置界面，包括基本设置、代码执行设置及关于信息
 * 
 * @author: WaterRun
 * @file: View/Settings.xaml.cs
 * @date: 2026-02-05
 */

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RunOnce.Static;
using Windows.System;

namespace RunOnce.View;

/// <summary>
/// 设置页面类，提供应用程序所有配置项的可视化编辑界面。
/// </summary>
/// <remarks>
/// 不变量：页面加载后所有控件状态与 Config 保持同步；用户操作立即持久化到配置。
/// 线程安全：所有成员必须在 UI 线程访问。
/// 副作用：控件值变更时自动更新 Config 并触发持久化。
/// </remarks>
public sealed partial class Settings : Page
{
    /// <summary>
    /// 编译时间戳，由编译时自动生成。
    /// </summary>
    private static readonly DateTime _buildTime = GetBuildTime();

    /// <summary>
    /// 标识是否正在进行程序化更新，用于避免事件循环触发。
    /// </summary>
    private bool _isUpdating;

    /// <summary>
    /// 初始化设置页面实例。
    /// </summary>
    /// <remarks>
    /// 执行顺序：初始化组件 → 加载本地化文本 → 初始化控件状态。
    /// </remarks>
    public Settings()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
    }

    /// <summary>
    /// 页面加载完成事件处理程序。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">路由事件参数。</param>
    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _isUpdating = true;

        LoadLocalizedTexts();
        InitializeComboBoxItems();
        LoadCurrentSettings();
        UpdateStoreRowVisibility();

        _isUpdating = false;
    }

    /// <summary>
    /// 加载所有界面元素的本地化文本。
    /// </summary>
    private void LoadLocalizedTexts()
    {
        BasicSectionHeader.Text = Text.Localize("基本");
        ExecutionSectionHeader.Text = Text.Localize("代码执行");
        AboutSectionHeader.Text = Text.Localize("此程序");

        ThemeLabel.Text = Text.Localize("外观");
        ThemeDescription.Text = Text.Localize("选择应用程序的主题风格");
        LanguageLabel.Text = Text.Localize("语言");
        LanguageDescription.Text = Text.Localize("选择应用程序的显示语言");

        ConfirmLabel.Text = Text.Localize("执行前确认");
        ConfirmDescription.Text = Text.Localize("执行代码前显示确认对话框");
        SelectorModeLabel.Text = Text.Localize("执行前语言选择框");
        SelectorModeDescription.Text = Text.Localize("控制语言选择框的显示时机");
        AutoExitLabel.Text = Text.Localize("执行后自动退出");
        AutoExitDescription.Text = Text.Localize("代码执行后自动关闭应用程序");
        TerminalLabel.Text = Text.Localize("终端类型");
        TerminalDescription.Text = Text.Localize("选择执行代码使用的终端程序");
        AdvancedSettingsLink.Content = Text.Localize("高级设置");

        AppNameLabel.Text = Text.Localize("软件名");
        AppNameValue.Text = Config.AppName;
        VersionLabel.Text = Text.Localize("版本");
        VersionValue.Text = Config.Version;
        BuildTimeLabel.Text = Text.Localize("编译于");
        BuildTimeValue.Text = _buildTime.ToString("yyyy-MM-dd HH:mm:ss");
        AuthorLabel.Text = Text.Localize("作者");
        AuthorValue.Text = Config.Author;
        GitHubLink.Content = Text.Localize("访问");
        StoreLabel.Text = Text.Localize("微软商店");
        StoreLink.Content = Text.Localize("访问");
        ResetAllLink.Content = Text.Localize("重置所有设置");
    }

    /// <summary>
    /// 初始化所有 ComboBox 控件的选项列表。
    /// </summary>
    private void InitializeComboBoxItems()
    {
        ThemeComboBox.Items.Clear();
        foreach (ThemeStyle theme in Enum.GetValues<ThemeStyle>())
        {
            ThemeComboBox.Items.Add(new ComboBoxItem
            {
                Content = Config.GetThemeDisplayName(theme),
                Tag = theme
            });
        }

        LanguageComboBox.Items.Clear();
        foreach (DisplayLanguage language in Enum.GetValues<DisplayLanguage>())
        {
            LanguageComboBox.Items.Add(new ComboBoxItem
            {
                Content = Config.GetLanguageDisplayName(language),
                Tag = language
            });
        }

        SelectorModeComboBox.Items.Clear();
        foreach (LanguageSelectorMode mode in Enum.GetValues<LanguageSelectorMode>())
        {
            SelectorModeComboBox.Items.Add(new ComboBoxItem
            {
                Content = Config.GetSelectorModeDisplayName(mode),
                Tag = mode
            });
        }

        TerminalComboBox.Items.Clear();
        foreach (TerminalType terminal in Enum.GetValues<TerminalType>())
        {
            TerminalComboBox.Items.Add(new ComboBoxItem
            {
                Content = Config.GetTerminalDisplayName(terminal),
                Tag = terminal
            });
        }
    }

    /// <summary>
    /// 从配置加载当前设置值到控件。
    /// </summary>
    private void LoadCurrentSettings()
    {
        SelectComboBoxItemByTag(ThemeComboBox, Config.Theme);
        SelectComboBoxItemByTag(LanguageComboBox, Config.Language);
        SelectComboBoxItemByTag(SelectorModeComboBox, Config.SelectorMode);
        SelectComboBoxItemByTag(TerminalComboBox, Config.Terminal);

        ConfirmToggle.IsOn = Config.ConfirmBeforeExecution;
        AutoExitToggle.IsOn = Config.AutoExitAfterExecution;
    }

    /// <summary>
    /// 根据 Tag 值选择 ComboBox 中对应的项。
    /// </summary>
    /// <typeparam name="T">Tag 值的类型。</typeparam>
    /// <param name="comboBox">目标 ComboBox 控件。</param>
    /// <param name="tagValue">要匹配的 Tag 值。</param>
    private static void SelectComboBoxItemByTag<T>(ComboBox comboBox, T tagValue) where T : notnull
    {
        ComboBoxItem? item = comboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Tag is T tag && EqualityComparer<T>.Default.Equals(tag, tagValue));

        if (item is not null)
        {
            comboBox.SelectedItem = item;
        }
    }

    /// <summary>
    /// 更新微软商店行的可见性。
    /// </summary>
    /// <remarks>
    /// 若商店链接为空，则隐藏该行及其分隔线。
    /// </remarks>
    private void UpdateStoreRowVisibility()
    {
        bool hasStoreUrl = !string.IsNullOrEmpty(Config.MicrosoftStoreUrl);
        StoreRow.Visibility = hasStoreUrl ? Visibility.Visible : Visibility.Collapsed;
        StoreDivider.Visibility = hasStoreUrl ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 主题选择变更事件处理程序。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">选择变更事件参数。</param>
    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdating || ThemeComboBox.SelectedItem is not ComboBoxItem { Tag: ThemeStyle theme })
        {
            return;
        }

        if (Application.Current is App app)
        {
            app.UpdateTheme(theme);
        }
    }

    /// <summary>
    /// 显示语言选择变更事件处理程序。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">选择变更事件参数。</param>
    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdating || LanguageComboBox.SelectedItem is not ComboBoxItem { Tag: DisplayLanguage language })
        {
            return;
        }

        Config.Language = language;
        LoadLocalizedTexts();
        InitializeComboBoxItems();
        LoadCurrentSettings();
    }

    /// <summary>
    /// 执行前确认开关切换事件处理程序。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">路由事件参数。</param>
    private void ConfirmToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isUpdating)
        {
            return;
        }

        Config.ConfirmBeforeExecution = ConfirmToggle.IsOn;
    }

    /// <summary>
    /// 语言选择框模式选择变更事件处理程序。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">选择变更事件参数。</param>
    private void SelectorModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdating || SelectorModeComboBox.SelectedItem is not ComboBoxItem { Tag: LanguageSelectorMode mode })
        {
            return;
        }

        Config.SelectorMode = mode;
    }

    /// <summary>
    /// 执行后自动退出开关切换事件处理程序。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">路由事件参数。</param>
    private void AutoExitToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isUpdating)
        {
            return;
        }

        Config.AutoExitAfterExecution = AutoExitToggle.IsOn;
    }

    /// <summary>
    /// 终端类型选择变更事件处理程序。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">选择变更事件参数。</param>
    private void TerminalComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdating || TerminalComboBox.SelectedItem is not ComboBoxItem { Tag: TerminalType terminal })
        {
            return;
        }

        Config.Terminal = terminal;
    }

    /// <summary>
    /// 高级设置链接点击事件处理程序。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">路由事件参数。</param>
    private async void AdvancedSettingsLink_Click(object sender, RoutedEventArgs e)
    {
        ContentDialog dialog = CreateAdvancedSettingsDialog();
        await dialog.ShowAsync();
    }

    /// <summary>
    /// 创建高级设置对话框。
    /// </summary>
    /// <returns>配置完成的 ContentDialog 实例。</returns>
    private ContentDialog CreateAdvancedSettingsDialog()
    {
        StackPanel content = new() { Spacing = 16, MinWidth = 400 };

        TextBox prefixTextBox = new()
        {
            Header = Text.Localize("临时文件名前缀"),
            Text = Config.TempFilePrefix,
            PlaceholderText = "__RunOnceTMP__"
        };
        content.Children.Add(prefixTextBox);

        StackPanel confidencePanel = new() { Spacing = 8 };
        confidencePanel.Children.Add(new TextBlock
        {
            Text = Text.Localize("置信度范围"),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"]
        });

        ConfidenceRange currentRange = Config.ConfidenceThreshold;

        TextBlock lowerValueText = new() { Text = currentRange.LowerBound.ToString("F2") };
        Slider lowerSlider = new()
        {
            Header = Text.Localize("下界"),
            Minimum = 0,
            Maximum = 1,
            StepFrequency = 0.01,
            Value = currentRange.LowerBound
        };
        lowerSlider.ValueChanged += (s, e) => lowerValueText.Text = e.NewValue.ToString("F2");

        StackPanel lowerPanel = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        lowerPanel.Children.Add(lowerSlider);
        lowerPanel.Children.Add(lowerValueText);

        TextBlock upperValueText = new() { Text = currentRange.UpperBound.ToString("F2") };
        Slider upperSlider = new()
        {
            Header = Text.Localize("上界"),
            Minimum = 0,
            Maximum = 1,
            StepFrequency = 0.01,
            Value = currentRange.UpperBound
        };
        upperSlider.ValueChanged += (s, e) => upperValueText.Text = e.NewValue.ToString("F2");

        StackPanel upperPanel = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        upperPanel.Children.Add(upperSlider);
        upperPanel.Children.Add(upperValueText);

        confidencePanel.Children.Add(lowerPanel);
        confidencePanel.Children.Add(upperPanel);
        content.Children.Add(confidencePanel);

        StackPanel languageCommandsPanel = new() { Spacing = 8 };
        languageCommandsPanel.Children.Add(new TextBlock
        {
            Text = Text.Localize("语言执行命令"),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"]
        });

        Dictionary<string, TextBox> commandTextBoxes = new();
        foreach (string language in Config.SupportedLanguages)
        {
            TextBox textBox = new()
            {
                Header = language.ToUpperInvariant(),
                Text = Config.GetLanguageCommand(language),
                PlaceholderText = Config.GetLanguageCommand(language)
            };
            commandTextBoxes[language] = textBox;
            languageCommandsPanel.Children.Add(textBox);
        }
        content.Children.Add(languageCommandsPanel);

        HyperlinkButton resetAdvancedLink = new()
        {
            Content = Text.Localize("重置为默认"),
            Padding = new Thickness(0)
        };
        resetAdvancedLink.Click += (s, e) =>
        {
            prefixTextBox.Text = "__RunOnceTMP__";
            lowerSlider.Value = ConfidenceRange.DefaultLowerBound;
            upperSlider.Value = ConfidenceRange.DefaultUpperBound;
            foreach (string language in Config.SupportedLanguages)
            {
                if (commandTextBoxes.TryGetValue(language, out TextBox? textBox))
                {
                    Config.ResetLanguageCommand(language);
                    textBox.Text = Config.GetLanguageCommand(language);
                }
            }
        };
        content.Children.Add(resetAdvancedLink);

        ScrollViewer scrollViewer = new()
        {
            Content = content,
            MaxHeight = 500,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto  // 修正此处
        };

        ContentDialog dialog = new()
        {
            Title = Text.Localize("高级设置"),
            Content = scrollViewer,
            PrimaryButtonText = Text.Localize("保存"),
            CloseButtonText = Text.Localize("取消"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        dialog.PrimaryButtonClick += (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(prefixTextBox.Text))
            {
                Config.TempFilePrefix = prefixTextBox.Text;
            }

            double lower = lowerSlider.Value;
            double upper = upperSlider.Value;
            if (lower <= upper)
            {
                Config.ConfidenceThreshold = new ConfidenceRange(lower, upper);
            }

            foreach (string language in Config.SupportedLanguages)
            {
                if (commandTextBoxes.TryGetValue(language, out TextBox? textBox) && !string.IsNullOrWhiteSpace(textBox.Text))
                {
                    Config.SetLanguageCommand(language, textBox.Text);
                }
            }
        };

        return dialog;
    }

    /// <summary>
    /// GitHub 链接点击事件处理程序。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">路由事件参数。</param>
    private async void GitHubLink_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(Config.GitHubUrl))
        {
            await Launcher.LaunchUriAsync(new Uri(Config.GitHubUrl));
        }
    }

    /// <summary>
    /// 微软商店链接点击事件处理程序。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">路由事件参数。</param>
    private async void StoreLink_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(Config.MicrosoftStoreUrl))
        {
            await Launcher.LaunchUriAsync(new Uri(Config.MicrosoftStoreUrl));
        }
    }

    /// <summary>
    /// 重置所有设置链接点击事件处理程序。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">路由事件参数。</param>
    private async void ResetAllLink_Click(object sender, RoutedEventArgs e)
    {
        ContentDialog confirmDialog = new()
        {
            Title = Text.Localize("重置所有设置"),
            Content = Text.Localize("确定要将所有设置重置为默认值吗？此操作无法撤销。"),
            PrimaryButtonText = Text.Localize("重置"),
            CloseButtonText = Text.Localize("取消"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        ContentDialogResult result = await confirmDialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            Config.ResetAllSettings();

            if (Application.Current is App app)
            {
                app.ApplyTheme(Config.Theme);
            }

            _isUpdating = true;
            LoadLocalizedTexts();
            InitializeComboBoxItems();
            LoadCurrentSettings();
            _isUpdating = false;
        }
    }

    /// <summary>
    /// 获取程序集的编译时间。
    /// </summary>
    /// <returns>编译时间的 DateTime 表示，若无法获取则返回当前时间。</returns>
    private static DateTime GetBuildTime()
    {
        System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
        System.Reflection.AssemblyInformationalVersionAttribute? attribute =
            assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                .FirstOrDefault();

        if (attribute?.InformationalVersion is string version)
        {
            int plusIndex = version.IndexOf('+');
            if (plusIndex >= 0 && version.Length > plusIndex + 1)
            {
                string commitInfo = version[(plusIndex + 1)..];
                if (commitInfo.Length >= 14 && DateTime.TryParseExact(
                    commitInfo[..14],
                    "yyyyMMddHHmmss",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out DateTime parsedTime))
                {
                    return parsedTime;
                }
            }
        }

        string? filePath = assembly.Location;
        if (!string.IsNullOrEmpty(filePath) && System.IO.File.Exists(filePath))
        {
            return System.IO.File.GetLastWriteTime(filePath);
        }

        return DateTime.Now;
    }
}