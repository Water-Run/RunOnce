/*
 * 设置页面视图
 * 提供应用程序配置界面的 View 层实现，负责本地化文本、对话框展示与主题应用
 *
 * @author: WaterRun
 * @file: View/Settings.xaml.cs
 * @date: 2026-03-19
 */

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RunOnce.Static;
using RunOnce.ViewModel;
using Windows.System;

namespace RunOnce.View;

/// <summary>
/// 设置页面，提供应用程序所有配置项的可视化编辑界面。
/// </summary>
public sealed partial class Settings : Page
{
    /// <summary>
    /// 设置页面的 ViewModel 实例。
    /// </summary>
    public SettingsViewModel ViewModel { get; }

    /// <summary>
    /// 初始化设置页面实例。
    /// </summary>
    public Settings()
    {
        ViewModel = new SettingsViewModel();
        ViewModel.ThemeChanged += OnThemeChanged;
        ViewModel.LanguageChanged += OnLanguageChanged;
        ViewModel.ScriptPlacementChangeRequested += OnScriptPlacementChangeRequested;
        ViewModel.PerformanceChangeRequested += OnPerformanceChangeRequested;
        InitializeComponent();
        Loaded += HandlePageLoaded;
    }

    /// <summary>
    /// 处理页面加载完成事件。
    /// </summary>
    private void HandlePageLoaded(object sender, RoutedEventArgs e)
    {
        ApplyLocalizedTexts();
        RefreshStoreRowVisibility();
    }

    #region 事件回调

    private static void OnThemeChanged(ThemeStyle theme)
    {
        if (Application.Current is App app)
        {
            app.ApplyTheme(theme);
        }
    }

    private void OnLanguageChanged()
    {
        ViewModel.RefreshAfterLanguageChange();
        ApplyLocalizedTexts();
        RefreshStoreRowVisibility();
    }

    private async void OnScriptPlacementChangeRequested(int oldIndex, int newIndex)
    {
        if (XamlRoot is null)
        {
            ViewModel.RevertScriptPlacement(oldIndex);
            return;
        }

        string message = (ScriptPlacementBehavior)newIndex switch
        {
            ScriptPlacementBehavior.EnsureCompatibility =>
                Text.Localize("此操作将把临时代码文件放置在工作目录，当异常关闭时，可能无法有效的清理。"),
            ScriptPlacementBehavior.EnsureCleanup =>
                Text.Localize("此操作将把临时代码文件放置在临时目录，可能产生一些兼容性问题。"),
            _ => string.Empty,
        };

        ContentDialog confirmDialog = new()
        {
            Title = Text.Localize("脚本放置行为"),
            Content = message,
            PrimaryButtonText = Text.Localize("确定"),
            CloseButtonText = Text.Localize("取消"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        ContentDialogResult result = await confirmDialog.ShowAsync();

        if (result is ContentDialogResult.Primary)
        {
            ViewModel.ConfirmScriptPlacement(newIndex);
        }
        else
        {
            ViewModel.RevertScriptPlacement(oldIndex);
        }
    }

    /// <summary>
    /// 处理 ViewModel 的编辑器性能策略变更请求，弹出确认对话框。
    /// </summary>
    private async void OnPerformanceChangeRequested(int oldIndex, int newIndex)
    {
        if (XamlRoot is null)
        {
            ViewModel.RevertPerformanceChange(oldIndex);
            return;
        }

        ContentDialog confirmDialog = new()
        {
            Title = Text.Localize("编辑器性能"),
            Content = Text.Localize("切换性能策略将清空编辑器中的所有内容，确定继续吗？"),
            PrimaryButtonText = Text.Localize("继续"),
            CloseButtonText = Text.Localize("取消"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        ContentDialogResult result = await confirmDialog.ShowAsync();

        if (result is ContentDialogResult.Primary)
        {
            ViewModel.ConfirmPerformanceChange(newIndex);
            ClearEditorContent();
        }
        else
        {
            ViewModel.RevertPerformanceChange(oldIndex);
        }
    }

    /// <summary>
    /// 请求编辑器页面清空所有内容。
    /// </summary>
    /// <remarks>
    /// 编辑器页面使用 NavigationCacheMode.Required，页面实例在导航后仍然存活。
    /// 通过 MainWindow 的 ContentFrame 的导航历史查找缓存的编辑器实例。
    /// </remarks>
    private static void ClearEditorContent()
    {
        if (Application.Current is not App { MainWindow: MainWindow mw })
        {
            return;
        }

        // 尝试从 ContentFrame 的 BackStack 中查找缓存的 Editor 实例
        // 由于 Editor 使用 NavigationCacheMode.Required，框架会保留其实例
        // 我们需要通过回退导航、清空、再前进的方式，或直接访问缓存
        // 最简单的方式：设置一个标志，在下次导航回编辑器时清空
        // 但这里采用直接方式：Frame 的 Content 如果是 Editor 则直接清空（仅当前页面就是 Editor 时）
        // 由于我们在 Settings 页面，Editor 不是当前 Content，所以需要用另一种方式

        // 通过 NavigationCacheMode.Required 的特性，Frame 内部会缓存页面实例
        // 我们可以在回到 Editor 时检查标志并清空
        // 这里使用静态标志的方式
        _pendingEditorClear = true;
    }

    /// <summary>
    /// 标识是否有待处理的编辑器清空请求。
    /// </summary>
    internal static bool _pendingEditorClear;

    #endregion

    #region 本地化文本

    private void ApplyLocalizedTexts()
    {
        PageTitle.Text = Text.Localize("设置");

        BasicSectionHeader.Text = Text.Localize("基本");
        ExecutionSectionHeader.Text = Text.Localize("代码执行");

        ThemeLabel.Text = Text.Localize("外观");
        ThemeDescription.Text = Text.Localize("选择应用程序的主题风格");
        LanguageLabel.Text = Text.Localize("语言");
        LanguageDescription.Text = Text.Localize("选择应用程序的显示语言");
        PerformanceLabel.Text = Text.Localize("编辑器性能");
        PerformanceDescription.Text = Text.Localize("调整语法高亮与语言检测的资源消耗级别");

        ConfirmLabel.Text = Text.Localize("执行前确认");
        ConfirmDescription.Text = Text.Localize("执行代码前显示确认对话框");
        SelectorModeLabel.Text = Text.Localize("执行前语言选择框");
        SelectorModeDescription.Text = Text.Localize("控制语言选择框的显示时机");
        AutoExitLabel.Text = Text.Localize("执行时自动退出");
        AutoExitDescription.Text = Text.Localize("开始执行代码后自动关闭应用程序");
        AutoCloseTerminalLabel.Text = Text.Localize("运行完毕后自动关闭终端");
        AutoCloseTerminalDescription.Text = Text.Localize("代码运行完成后自动关闭终端窗口");
        ShellLabel.Text = Text.Localize("运行环境");
        ShellDescription.Text = Text.Localize("选择执行代码使用的命令解释器");
        ScriptPlacementLabel.Text = Text.Localize("脚本放置行为");
        ScriptPlacementDescription.Text = Text.Localize("选择临时代码文件的放置位置");
        ShortcutsLabel.Text = Text.Localize("快捷键");
        ShortcutsDescription.Text = Text.Localize("查看应用程序支持的快捷键");
        ShortcutsButton.Content = Text.Localize("查看");
        AdvancedSettingsLabel.Text = Text.Localize("高级设置");
        AdvancedSettingsDescription.Text = Text.Localize("配置临时文件、置信度阈值和语言命令");
        AdvancedSettingsButton.Content = Text.Localize("打开");
        AiSettingsLabel.Text = Text.Localize("AI 设置");
        AiSettingsDescription.Text = Text.Localize("配置 LLM API 以生成脚本代码");
        AiSettingsButton.Content = Text.Localize("打开");

        ApplyWideLocalizedTexts();
        ApplyNarrowAboutLocalizedTexts();
    }

    private void ApplyWideLocalizedTexts()
    {
        WideStoreLink.Content = Text.Localize("微软商店");
        WideResetLink.Content = Text.Localize("重置所有设置");
    }

    private void ApplyNarrowAboutLocalizedTexts()
    {
        NarrowAboutSectionHeader.Text = Text.Localize("此程序");
        NarrowAppNameLabel.Text = Text.Localize("软件名");
        NarrowVersionLabel.Text = Text.Localize("版本");
        NarrowBuildTimeLabel.Text = Text.Localize("编译于");
        NarrowAuthorLabel.Text = Text.Localize("作者");
        NarrowGitHubLink.Content = Text.Localize("访问");
        NarrowStoreLabel.Text = Text.Localize("微软商店");
        NarrowStoreLink.Content = Text.Localize("访问");
        NarrowResetLink.Content = Text.Localize("重置所有设置");
    }

    #endregion

    #region 可见性管理

    private void RefreshStoreRowVisibility()
    {
        Visibility storeVisibility = ViewModel.HasStoreUrl ? Visibility.Visible : Visibility.Collapsed;
        NarrowStoreRow.Visibility = storeVisibility;
        WideStoreLink.Visibility = storeVisibility;
    }

    #endregion

    #region 快捷键对话框

    private async void ShortcutsButton_Click(object sender, RoutedEventArgs e)
    {
        ContentDialog dialog = BuildShortcutsDialog();
        await dialog.ShowAsync();
    }

    private ContentDialog BuildShortcutsDialog()
    {
        StackPanel panel = new() { Spacing = 8, MinWidth = 380 };

        AddShortcutRow(panel, "Ctrl+Enter", Text.Localize("执行代码"));
        AddShortcutRow(panel, "Ctrl+G", Text.Localize("AI 生成代码"));
        AddShortcutRow(panel, "Ctrl+E", Text.Localize("命令行参数"));
        AddShortcutRow(panel, "Ctrl+Y", Text.Localize("重做"));
        AddShortcutRow(panel, "Ctrl+A", Text.Localize("全选"));
        AddShortcutRow(panel, "Ctrl+C", Text.Localize("复制"));
        AddShortcutRow(panel, "Ctrl+V", Text.Localize("粘贴"));
        AddShortcutRow(panel, "Ctrl+X", Text.Localize("剪切"));
        AddShortcutRow(panel, "Tab", Text.Localize("缩进"));
        AddShortcutRow(panel, "Shift+Tab", Text.Localize("减少缩进"));

        return new ContentDialog
        {
            Title = Text.Localize("快捷键"),
            Content = panel,
            CloseButtonText = Text.Localize("关闭"),
            XamlRoot = XamlRoot,
        };
    }

    private static void AddShortcutRow(StackPanel panel, string shortcut, string description)
    {
        Grid row = new() { Padding = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        TextBlock keyBlock = new()
        {
            Text = shortcut,
            FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New, monospace"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.85,
        };
        Grid.SetColumn(keyBlock, 0);
        row.Children.Add(keyBlock);

        TextBlock descBlock = new()
        {
            Text = description,
            Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(descBlock, 1);
        row.Children.Add(descBlock);

        panel.Children.Add(row);
    }

    #endregion

    #region 高级设置对话框

    private async void AdvancedSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ContentDialog dialog = BuildAdvancedSettingsDialog();
        await dialog.ShowAsync();
    }

    private ContentDialog BuildAdvancedSettingsDialog()
    {
        StackPanel contentPanel = new() { Spacing = 16, MinWidth = 450, Margin = new Thickness(0, 0, 8, 0) };

        TextBox prefixTextBox = new()
        {
            Header = Text.Localize("临时文件名前缀"),
            Text = ViewModel.GetTempFilePrefix(),
            PlaceholderText = Config.DefaultTempFilePrefix,
        };
        contentPanel.Children.Add(prefixTextBox);

        StackPanel thresholdPanel = new() { Spacing = 8 };
        thresholdPanel.Children.Add(new TextBlock
        {
            Text = Text.Localize("置信度阈值"),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });
        NumberBox thresholdBox = new()
        {
            Value = ViewModel.GetConfidenceThreshold(),
            Minimum = 0,
            Maximum = 1,
            SmallChange = 0.01,
            LargeChange = 0.1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        thresholdPanel.Children.Add(thresholdBox);
        contentPanel.Children.Add(thresholdPanel);

        (StackPanel commandsPanel, Dictionary<string, TextBox> commandTextBoxes) = BuildLanguageCommandControls();
        contentPanel.Children.Add(commandsPanel);

        HyperlinkButton resetLink = BuildResetAdvancedLink(prefixTextBox, thresholdBox, commandTextBoxes);
        contentPanel.Children.Add(resetLink);

        TextBlock errorText = new()
        {
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 4, 0, 0),
        };
        contentPanel.Children.Add(errorText);

        ScrollViewer scrollViewer = new()
        {
            Content = contentPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = Math.Max(200, XamlRoot.Size.Height - 200),
            Padding = new Thickness(0, 0, 16, 0),
        };

        ContentDialog dialog = new()
        {
            Title = Text.Localize("高级设置"),
            Content = scrollViewer,
            PrimaryButtonText = Text.Localize("保存"),
            CloseButtonText = Text.Localize("取消"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        dialog.PrimaryButtonClick += (_, args) =>
        {
            errorText.Visibility = Visibility.Collapsed;

            try
            {
                Dictionary<string, string> commands = commandTextBoxes.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Text);

                ViewModel.SaveAdvancedSettings(prefixTextBox.Text, thresholdBox.Value, commands);
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                errorText.Text = ex.Message;
                errorText.Visibility = Visibility.Visible;
            }
        };

        return dialog;
    }

    private static (StackPanel Panel, Dictionary<string, TextBox> TextBoxes) BuildLanguageCommandControls()
    {
        StackPanel panel = new() { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = Text.Localize("语言执行命令"),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });

        Dictionary<string, string> currentCommands = Config.GetAllLanguageCommands();

        Dictionary<string, TextBox> textBoxes = Config.SupportedLanguages.ToDictionary(
            language => language,
            language =>
            {
                string currentCommand = currentCommands.GetValueOrDefault(language, language);
                TextBox textBox = new()
                {
                    Header = new TextBlock
                    {
                        Text = language.ToUpperInvariant(),
                        FontSize = 13,
                        FontStyle = Windows.UI.Text.FontStyle.Italic,
                        Opacity = 0.6,
                    },
                    Text = currentCommand,
                    PlaceholderText = currentCommand,
                };
                panel.Children.Add(textBox);
                return textBox;
            });

        return (panel, textBoxes);
    }

    private HyperlinkButton BuildResetAdvancedLink(
        TextBox prefixTextBox,
        NumberBox thresholdBox,
        Dictionary<string, TextBox> commandTextBoxes)
    {
        HyperlinkButton resetLink = new()
        {
            Content = Text.Localize("重置为默认"),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        resetLink.Click += (_, _) =>
        {
            (string prefix, double threshold, Dictionary<string, string> commands) = ViewModel.ResetAdvancedToDefaults();

            prefixTextBox.Text = prefix;
            thresholdBox.Value = threshold;

            foreach ((string language, TextBox textBox) in commandTextBoxes)
            {
                textBox.Text = commands.GetValueOrDefault(language, language);
            }
        };

        return resetLink;
    }

    private async void ResetAllLink_Click(object sender, RoutedEventArgs e)
    {
        ContentDialog confirmDialog = new()
        {
            Title = Text.Localize("重置所有设置"),
            Content = Text.Localize("确定要将所有设置重置为默认值吗？此操作无法撤销。"),
            PrimaryButtonText = Text.Localize("重置"),
            CloseButtonText = Text.Localize("取消"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        ContentDialogResult result = await confirmDialog.ShowAsync();
        if (result is not ContentDialogResult.Primary)
        {
            return;
        }

        ViewModel.ResetAllSettings();

        if (Application.Current is App app)
        {
            app.ApplyTheme(Config.Theme);
        }

        ApplyLocalizedTexts();
        RefreshStoreRowVisibility();
    }

    #endregion

    #region AI 设置对话框

    private async void AiSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ContentDialog dialog = BuildAiSettingsDialog();
        await dialog.ShowAsync();
    }

    private ContentDialog BuildAiSettingsDialog()
    {
        StackPanel contentPanel = new() { Spacing = 16, MinWidth = 450, Margin = new Thickness(0, 0, 8, 0) };

        PasswordBox apiKeyBox = new()
        {
            Header = Text.Localize("API Key"),
            PlaceholderText = Text.Localize("输入 API Key"),
            Password = Config.LlmApiKey,
        };
        contentPanel.Children.Add(apiKeyBox);

        TextBox baseUrlBox = new()
        {
            Header = Text.Localize("API 基础 URL"),
            Text = Config.LlmBaseUrl,
            PlaceholderText = Config.DefaultLlmBaseUrl,
        };
        contentPanel.Children.Add(baseUrlBox);

        TextBox modelBox = new()
        {
            Header = Text.Localize("模型名称"),
            Text = Config.LlmModel,
            PlaceholderText = Config.DefaultLlmModel,
        };
        contentPanel.Children.Add(modelBox);

        NumberBox maxTokensBox = new()
        {
            Header = Text.Localize("最大 Token 数"),
            Value = Config.LlmMaxTokens,
            Minimum = 256,
            Maximum = 32768,
            SmallChange = 256,
            LargeChange = 1024,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        contentPanel.Children.Add(maxTokensBox);

        NumberBox timeoutBox = new()
        {
            Header = Text.Localize("请求超时（秒）"),
            Value = Config.LlmTimeoutSeconds,
            Minimum = 10,
            Maximum = 300,
            SmallChange = 10,
            LargeChange = 30,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        contentPanel.Children.Add(timeoutBox);

        HyperlinkButton resetLink = new()
        {
            Content = Text.Localize("重置为默认"),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        resetLink.Click += (_, _) =>
        {
            apiKeyBox.Password = string.Empty;
            baseUrlBox.Text = Config.DefaultLlmBaseUrl;
            modelBox.Text = Config.DefaultLlmModel;
            maxTokensBox.Value = Config.DefaultLlmMaxTokens;
            timeoutBox.Value = Config.DefaultLlmTimeoutSeconds;
        };
        contentPanel.Children.Add(resetLink);

        TextBlock errorText = new()
        {
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 4, 0, 0),
        };
        contentPanel.Children.Add(errorText);

        ScrollViewer scrollViewer = new()
        {
            Content = contentPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = Math.Max(200, XamlRoot.Size.Height - 200),
            Padding = new Thickness(0, 0, 16, 0),
        };

        ContentDialog dialog = new()
        {
            Title = Text.Localize("AI 设置"),
            Content = scrollViewer,
            PrimaryButtonText = Text.Localize("保存"),
            CloseButtonText = Text.Localize("取消"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        dialog.PrimaryButtonClick += (_, args) =>
        {
            errorText.Visibility = Visibility.Collapsed;
            try
            {
                Config.LlmApiKey = apiKeyBox.Password;
                Config.LlmBaseUrl = string.IsNullOrWhiteSpace(baseUrlBox.Text)
                    ? Config.DefaultLlmBaseUrl
                    : baseUrlBox.Text;
                Config.LlmModel = string.IsNullOrWhiteSpace(modelBox.Text)
                    ? Config.DefaultLlmModel
                    : modelBox.Text;
                if (!double.IsNaN(maxTokensBox.Value) && maxTokensBox.Value > 0)
                {
                    Config.LlmMaxTokens = (int)maxTokensBox.Value;
                }
                if (!double.IsNaN(timeoutBox.Value) && timeoutBox.Value > 0)
                {
                    Config.LlmTimeoutSeconds = (int)timeoutBox.Value;
                }
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                errorText.Text = ex.Message;
                errorText.Visibility = Visibility.Visible;
            }
        };

        return dialog;
    }

    #endregion

    #region 外部链接

    private async void GitHubLink_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(ViewModel.GitHubUrl))
        {
            await Launcher.LaunchUriAsync(new Uri(ViewModel.GitHubUrl));
        }
    }

    private async void StoreLink_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.HasStoreUrl)
        {
            await Launcher.LaunchUriAsync(new Uri(ViewModel.StoreUrl));
        }
    }

    #endregion
}