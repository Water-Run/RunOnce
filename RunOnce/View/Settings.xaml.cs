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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RunOnce.Static;
using Windows.System;

namespace RunOnce.View;

/// <summary>
/// 设置页面，提供应用程序所有配置项的可视化编辑界面。
/// </summary>
/// <remarks>
/// 不变量：页面加载后所有控件状态与 <see cref="Config"/> 保持同步；用户操作立即持久化到配置。
/// 线程安全：所有成员必须在 UI 线程访问。
/// 副作用：控件值变更时自动更新 <see cref="Config"/> 并触发持久化。
/// </remarks>
public sealed partial class Settings : Page
{
    /// <summary>
    /// 编译时间戳，由编译时自动生成。
    /// </summary>
    /// <remarks>
    /// 生命周期：静态只读，应用程序生命周期内不变。
    /// </remarks>
    private static readonly DateTime _buildTime = RetrieveBuildTime();

    /// <summary>
    /// 标识是否正在进行程序化更新，用于避免事件循环触发。
    /// </summary>
    /// <remarks>
    /// 当为 true 时，所有控件事件处理程序应跳过配置更新逻辑。
    /// </remarks>
    private bool _isProgrammaticUpdate;

    /// <summary>
    /// 初始化设置页面实例。
    /// </summary>
    /// <remarks>
    /// 执行顺序：初始化组件 → 注册加载事件。
    /// </remarks>
    public Settings()
    {
        InitializeComponent();
        Loaded += HandlePageLoaded;
    }

    /// <summary>
    /// 处理页面加载完成事件。
    /// </summary>
    /// <param name="sender">事件源对象，预期为当前页面实例。</param>
    /// <param name="e">路由事件参数。</param>
    private void HandlePageLoaded(object sender, RoutedEventArgs e)
    {
        _isProgrammaticUpdate = true;

        ApplyLocalizedTexts();
        PopulateComboBoxItems();
        SynchronizeControlsWithConfig();
        RefreshStoreRowVisibility();
        UpdateAboutSectionCornerRadius();

        _isProgrammaticUpdate = false;
    }

    /// <summary>
    /// 应用所有界面元素的本地化文本。
    /// </summary>
    /// <remarks>
    /// 副作用：更新所有 TextBlock 和控件的显示文本。
    /// </remarks>
    private void ApplyLocalizedTexts()
    {
        PageTitle.Text = Text.Localize("设置");

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
        AdvancedSettingsLabel.Text = Text.Localize("高级设置");
        AdvancedSettingsDescription.Text = Text.Localize("配置临时文件、置信度阈值和语言命令");
        AdvancedSettingsButton.Content = Text.Localize("打开");

        AppNameLabel.Text = Text.Localize("软件名");
        AppNameValue.Text = Config.AppName;
        VersionLabel.Text = Text.Localize("版本");
        VersionValue.Text = Config.Version;
        BuildTimeLabel.Text = Text.Localize("编译于");
        BuildTimeValue.Text = _buildTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        AuthorLabel.Text = Text.Localize("作者");
        AuthorValue.Text = Config.Author;
        GitHubLink.Content = Text.Localize("访问");
        StoreLabel.Text = Text.Localize("微软商店");
        StoreLink.Content = Text.Localize("访问");
        ResetAllLink.Content = Text.Localize("重置所有设置");
    }

    /// <summary>
    /// 填充所有 ComboBox 控件的选项列表。
    /// </summary>
    /// <remarks>
    /// 副作用：清空并重新填充所有 ComboBox 的 Items 集合。
    /// </remarks>
    private void PopulateComboBoxItems()
    {
        PopulateSingleComboBox(ThemeComboBox, Enum.GetValues<ThemeStyle>(), Config.GetThemeDisplayName);
        PopulateSingleComboBox(LanguageComboBox, Enum.GetValues<DisplayLanguage>(), Config.GetLanguageDisplayName);
        PopulateSingleComboBox(SelectorModeComboBox, Enum.GetValues<LanguageSelectorMode>(), Config.GetSelectorModeDisplayName);
        PopulateSingleComboBox(TerminalComboBox, Enum.GetValues<TerminalType>(), Config.GetTerminalDisplayName);
    }

    /// <summary>
    /// 填充单个 ComboBox 的选项。
    /// </summary>
    /// <typeparam name="T">枚举类型。</typeparam>
    /// <param name="comboBox">目标 ComboBox 控件，不允许为 null。</param>
    /// <param name="values">枚举值集合，不允许为 null。</param>
    /// <param name="displayNameSelector">获取显示名称的委托，不允许为 null。</param>
    private static void PopulateSingleComboBox<T>(ComboBox comboBox, IEnumerable<T> values, Func<T, string> displayNameSelector) where T : Enum
    {
        comboBox.Items.Clear();
        foreach (T value in values)
        {
            comboBox.Items.Add(new ComboBoxItem
            {
                Content = displayNameSelector(value),
                Tag = value
            });
        }
    }

    /// <summary>
    /// 将控件状态与当前配置同步。
    /// </summary>
    /// <remarks>
    /// 副作用：更新所有设置控件的选中状态和开关状态。
    /// </remarks>
    private void SynchronizeControlsWithConfig()
    {
        SelectItemByTag(ThemeComboBox, Config.Theme);
        SelectItemByTag(LanguageComboBox, Config.Language);
        SelectItemByTag(SelectorModeComboBox, Config.SelectorMode);
        SelectItemByTag(TerminalComboBox, Config.Terminal);

        ConfirmToggle.IsOn = Config.ConfirmBeforeExecution;
        AutoExitToggle.IsOn = Config.AutoExitAfterExecution;
    }

    /// <summary>
    /// 根据 Tag 值选择 ComboBox 中对应的项。
    /// </summary>
    /// <typeparam name="T">Tag 值的类型，必须实现值相等比较。</typeparam>
    /// <param name="comboBox">目标 ComboBox 控件，不允许为 null。</param>
    /// <param name="targetTag">要匹配的 Tag 值。</param>
    private static void SelectItemByTag<T>(ComboBox comboBox, T targetTag) where T : notnull
    {
        ComboBoxItem? matchingItem = comboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag is T tag && EqualityComparer<T>.Default.Equals(tag, targetTag));

        if (matchingItem is not null)
        {
            comboBox.SelectedItem = matchingItem;
        }
    }

    /// <summary>
    /// 刷新微软商店行的可见性。
    /// </summary>
    /// <remarks>
    /// 若商店链接为空，则隐藏该行。
    /// </remarks>
    private void RefreshStoreRowVisibility()
    {
        StoreRow.Visibility = string.IsNullOrEmpty(Config.MicrosoftStoreUrl)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    /// <summary>
    /// 更新关于区域最后一个可见卡片的圆角。
    /// </summary>
    /// <remarks>
    /// 根据商店行的可见性动态调整圆角，确保最后一个卡片有底部圆角。
    /// </remarks>
    private void UpdateAboutSectionCornerRadius()
    {
        bool storeVisible = StoreRow.Visibility == Visibility.Visible;

        StoreRow.CornerRadius = new CornerRadius(0);
        ResetRow.CornerRadius = new CornerRadius(0, 0, 4, 4);

        if (storeVisible)
        {
            StoreRow.BorderThickness = new Thickness(1, 0, 1, 1);
        }
    }

    /// <summary>
    /// 处理主题选择变更事件。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">选择变更事件参数。</param>
    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isProgrammaticUpdate)
        {
            return;
        }

        if (ThemeComboBox.SelectedItem is ComboBoxItem { Tag: ThemeStyle theme } && Application.Current is App app)
        {
            app.UpdateTheme(theme);
        }
    }

    /// <summary>
    /// 处理显示语言选择变更事件。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">选择变更事件参数。</param>
    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isProgrammaticUpdate)
        {
            return;
        }

        if (LanguageComboBox.SelectedItem is not ComboBoxItem { Tag: DisplayLanguage language })
        {
            return;
        }

        Config.Language = language;

        _isProgrammaticUpdate = true;
        ApplyLocalizedTexts();
        PopulateComboBoxItems();
        SynchronizeControlsWithConfig();
        _isProgrammaticUpdate = false;
    }

    /// <summary>
    /// 处理执行前确认开关切换事件。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">路由事件参数。</param>
    private void ConfirmToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_isProgrammaticUpdate)
        {
            Config.ConfirmBeforeExecution = ConfirmToggle.IsOn;
        }
    }

    /// <summary>
    /// 处理语言选择框模式选择变更事件。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">选择变更事件参数。</param>
    private void SelectorModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isProgrammaticUpdate && SelectorModeComboBox.SelectedItem is ComboBoxItem { Tag: LanguageSelectorMode mode })
        {
            Config.SelectorMode = mode;
        }
    }

    /// <summary>
    /// 处理执行后自动退出开关切换事件。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">路由事件参数。</param>
    private void AutoExitToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_isProgrammaticUpdate)
        {
            Config.AutoExitAfterExecution = AutoExitToggle.IsOn;
        }
    }

    /// <summary>
    /// 处理终端类型选择变更事件。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">选择变更事件参数。</param>
    private void TerminalComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isProgrammaticUpdate && TerminalComboBox.SelectedItem is ComboBoxItem { Tag: TerminalType terminal })
        {
            Config.Terminal = terminal;
        }
    }

    /// <summary>
    /// 处理高级设置链接点击事件。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">路由事件参数。</param>
    private async void AdvancedSettingsLink_Click(object sender, RoutedEventArgs e)
    {
        ContentDialog dialog = BuildAdvancedSettingsDialog();
        await dialog.ShowAsync();
    }

    /// <summary>
    /// 构建高级设置对话框。
    /// </summary>
    /// <returns>配置完成的 <see cref="ContentDialog"/> 实例。</returns>
    private ContentDialog BuildAdvancedSettingsDialog()
    {
        StackPanel contentPanel = new() { Spacing = 16, MinWidth = 450, Margin = new Thickness(0, 0, 8, 0) };

        TextBox prefixTextBox = new()
        {
            Header = Text.Localize("临时文件名前缀"),
            Text = Config.TempFilePrefix,
            PlaceholderText = "__RunOnceTMP__"
        };
        contentPanel.Children.Add(prefixTextBox);

        (StackPanel confidencePanel, NumberBox lowerBox, NumberBox upperBox) = BuildConfidenceRangeControls();
        contentPanel.Children.Add(confidencePanel);

        (StackPanel commandsPanel, Dictionary<string, TextBox> commandTextBoxes) = BuildLanguageCommandControls();
        contentPanel.Children.Add(commandsPanel);

        HyperlinkButton resetLink = BuildResetAdvancedLink(prefixTextBox, lowerBox, upperBox, commandTextBoxes);
        contentPanel.Children.Add(resetLink);

        ScrollViewer scrollViewer = new()
        {
            Content = contentPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 0, 16, 0)
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

        dialog.PrimaryButtonClick += (_, _) => SaveAdvancedSettings(prefixTextBox, lowerBox, upperBox, commandTextBoxes);

        return dialog;
    }

    /// <summary>
    /// 构建置信度范围控件组。
    /// </summary>
    /// <returns>包含面板、下界数值框和上界数值框的元组。</returns>
    private static (StackPanel Panel, NumberBox LowerBox, NumberBox UpperBox) BuildConfidenceRangeControls()
    {
        StackPanel panel = new() { Spacing = 12 };

        panel.Children.Add(new TextBlock
        {
            Text = Text.Localize("置信度范围"),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"]
        });

        ConfidenceRange currentRange = Config.ConfidenceThreshold;

        Grid rangeGrid = new() { ColumnSpacing = 16 };
        rangeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rangeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        NumberBox lowerBox = new()
        {
            Header = Text.Localize("下界"),
            Value = currentRange.LowerBound,
            Minimum = 0,
            Maximum = 1,
            SmallChange = 0.01,
            LargeChange = 0.1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        Grid.SetColumn(lowerBox, 0);

        NumberBox upperBox = new()
        {
            Header = Text.Localize("上界"),
            Value = currentRange.UpperBound,
            Minimum = 0,
            Maximum = 1,
            SmallChange = 0.01,
            LargeChange = 0.1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        Grid.SetColumn(upperBox, 1);

        rangeGrid.Children.Add(lowerBox);
        rangeGrid.Children.Add(upperBox);
        panel.Children.Add(rangeGrid);

        return (panel, lowerBox, upperBox);
    }

    /// <summary>
    /// 创建置信度滑块控件。
    /// </summary>
    /// <param name="header">滑块标题文本。</param>
    /// <param name="initialValue">初始值，范围 [0, 1]。</param>
    /// <returns>配置完成的 <see cref="Slider"/> 实例。</returns>
    private static Slider CreateConfidenceSlider(string header, double initialValue)
    {
        return new Slider
        {
            Header = header,
            Minimum = 0,
            Maximum = 1,
            StepFrequency = 0.01,
            Value = initialValue
        };
    }

    /// <summary>
    /// 构建语言执行命令控件组。
    /// </summary>
    /// <returns>包含面板和命令文本框字典的元组。</returns>
    private static (StackPanel Panel, Dictionary<string, TextBox> TextBoxes) BuildLanguageCommandControls()
    {
        StackPanel panel = new() { Spacing = 8 };

        panel.Children.Add(new TextBlock
        {
            Text = Text.Localize("语言执行命令"),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"]
        });

        Dictionary<string, TextBox> textBoxes = Config.SupportedLanguages
            .ToDictionary(
                language => language,
                language =>
                {
                    TextBox textBox = new()
                    {
                        Header = language.ToUpperInvariant(),
                        Text = Config.GetLanguageCommand(language),
                        PlaceholderText = Config.GetLanguageCommand(language)
                    };
                    panel.Children.Add(textBox);
                    return textBox;
                });

        return (panel, textBoxes);
    }

    /// <summary>
    /// 构建重置高级设置链接按钮。
    /// </summary>
    /// <param name="prefixTextBox">临时文件前缀文本框。</param>
    /// <param name="lowerBox">置信度下界数值框。</param>
    /// <param name="upperBox">置信度上界数值框。</param>
    /// <param name="commandTextBoxes">语言命令文本框字典。</param>
    /// <returns>配置完成的 <see cref="HyperlinkButton"/> 实例。</returns>
    private static HyperlinkButton BuildResetAdvancedLink(
        TextBox prefixTextBox,
        NumberBox lowerBox,
        NumberBox upperBox,
        Dictionary<string, TextBox> commandTextBoxes)
    {
        HyperlinkButton resetLink = new()
        {
            Content = Text.Localize("重置为默认"),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right
        };

        resetLink.Click += (_, _) =>
        {
            prefixTextBox.Text = "__RunOnceTMP__";
            lowerBox.Value = ConfidenceRange.DefaultLowerBound;
            upperBox.Value = ConfidenceRange.DefaultUpperBound;

            foreach (string language in Config.SupportedLanguages)
            {
                Config.ResetLanguageCommand(language);
                if (commandTextBoxes.TryGetValue(language, out TextBox? textBox))
                {
                    textBox.Text = Config.GetLanguageCommand(language);
                }
            }
        };

        return resetLink;
    }

    /// <summary>
    /// 保存高级设置到配置。
    /// </summary>
    /// <param name="prefixTextBox">临时文件前缀文本框。</param>
    /// <param name="lowerBox">置信度下界数值框。</param>
    /// <param name="upperBox">置信度上界数值框。</param>
    /// <param name="commandTextBoxes">语言命令文本框字典。</param>
    private static void SaveAdvancedSettings(
        TextBox prefixTextBox,
        NumberBox lowerBox,
        NumberBox upperBox,
        Dictionary<string, TextBox> commandTextBoxes)
    {
        if (!string.IsNullOrWhiteSpace(prefixTextBox.Text))
        {
            Config.TempFilePrefix = prefixTextBox.Text;
        }

        double lower = lowerBox.Value;
        double upper = upperBox.Value;
        if (!double.IsNaN(lower) && !double.IsNaN(upper) && lower <= upper)
        {
            Config.ConfidenceThreshold = new ConfidenceRange(lower, upper);
        }

        foreach ((string language, TextBox textBox) in commandTextBoxes)
        {
            if (!string.IsNullOrWhiteSpace(textBox.Text))
            {
                Config.SetLanguageCommand(language, textBox.Text);
            }
        }
    }

    /// <summary>
    /// 处理 GitHub 链接点击事件。
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
    /// 处理微软商店链接点击事件。
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
    /// 处理重置所有设置链接点击事件。
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
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        Config.ResetAllSettings();

        if (Application.Current is App app)
        {
            app.ApplyTheme(Config.Theme);
        }

        _isProgrammaticUpdate = true;
        ApplyLocalizedTexts();
        PopulateComboBoxItems();
        SynchronizeControlsWithConfig();
        _isProgrammaticUpdate = false;
    }

    /// <summary>
    /// 获取程序集的编译时间。
    /// </summary>
    /// <returns>编译时间的 <see cref="DateTime"/> 表示，若无法获取则返回当前时间。</returns>
    private static DateTime RetrieveBuildTime()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();

        DateTime? versionBasedTime = TryGetBuildTimeFromVersion(assembly);
        if (versionBasedTime.HasValue)
        {
            return versionBasedTime.Value;
        }

        DateTime? fileBasedTime = TryGetBuildTimeFromFile(assembly);
        if (fileBasedTime.HasValue)
        {
            return fileBasedTime.Value;
        }

        return DateTime.Now;
    }

    /// <summary>
    /// 尝试从程序集版本信息获取编译时间。
    /// </summary>
    /// <param name="assembly">目标程序集，不允许为 null。</param>
    /// <returns>解析成功时返回编译时间，否则返回 null。</returns>
    private static DateTime? TryGetBuildTimeFromVersion(Assembly assembly)
    {
        AssemblyInformationalVersionAttribute? attribute = assembly
            .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
            .OfType<AssemblyInformationalVersionAttribute>()
            .FirstOrDefault();

        if (attribute?.InformationalVersion is not { } version)
        {
            return null;
        }

        int plusIndex = version.IndexOf('+');
        if (plusIndex < 0 || version.Length <= plusIndex + 14)
        {
            return null;
        }

        string timestampPart = version[(plusIndex + 1)..(plusIndex + 15)];

        return DateTime.TryParseExact(
            timestampPart,
            "yyyyMMddHHmmss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateTime parsedTime)
            ? parsedTime
            : null;
    }

    /// <summary>
    /// 尝试从程序集文件获取编译时间。
    /// </summary>
    /// <param name="assembly">目标程序集，不允许为 null。</param>
    /// <returns>文件存在时返回最后修改时间，否则返回 null。</returns>
    private static DateTime? TryGetBuildTimeFromFile(Assembly assembly)
    {
        string? filePath = assembly.Location;

        return !string.IsNullOrEmpty(filePath) && File.Exists(filePath)
            ? File.GetLastWriteTime(filePath)
            : null;
    }
}