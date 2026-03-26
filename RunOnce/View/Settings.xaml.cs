/*
 * 设置页面视图
 * 提供应用程序配置界面的 View 层实现，负责本地化文本、对话框展示与主题应用
 *
 * @author: WaterRun
 * @file: View/Settings.xaml.cs
 * @date: 2026-03-26
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
/// <remarks>
/// 不变量：<see cref="ViewModel"/> 在构造后非空，生命周期与页面实例绑定。
/// 线程安全：不线程安全，所有成员须在 UI 线程上访问。
/// 副作用：构造时订阅 <see cref="SettingsViewModel"/> 的主题、语言、脚本放置与性能变更事件；页面销毁后事件订阅不会自动移除。
/// 使用约束：须由 WinUI 导航框架实例化，不应手动构造。
/// </remarks>
public sealed partial class Settings : Page
{
    /// <summary>
    /// 设置页面绑定的 ViewModel，负责所有配置项的读取、变更与持久化逻辑。
    /// </summary>
    /// <value>非空，构造时初始化，生命周期与页面相同，禁止外部替换。</value>
    public SettingsViewModel ViewModel { get; }

    /// <summary>
    /// 初始化设置页面实例，创建 <see cref="ViewModel"/> 并注册所有 ViewModel 事件回调。
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
    /// 处理页面加载完成事件，依次执行本地化文本应用与商店行可见性刷新。
    /// </summary>
    /// <param name="sender">事件发送方，通常为当前页面实例，非空。</param>
    /// <param name="e">路由事件参数，非空。</param>
    private void HandlePageLoaded(object sender, RoutedEventArgs e)
    {
        ApplyLocalizedTexts();
        RefreshStoreRowVisibility();
    }

    #region 事件回调

    /// <summary>
    /// 响应 ViewModel 主题变更通知，将新主题应用到应用程序全局。
    /// </summary>
    /// <param name="theme">目标主题风格，须在 <see cref="ThemeStyle"/> 枚举范围内，不允许超出定义。</param>
    private static void OnThemeChanged(ThemeStyle theme)
    {
        if (Application.Current is App app)
        {
            app.ApplyTheme(theme);
        }
    }

    /// <summary>
    /// 响应 ViewModel 语言变更通知，刷新 ViewModel 数据并重新应用本地化文本与商店行可见性。
    /// </summary>
    private void OnLanguageChanged()
    {
        ViewModel.RefreshAfterLanguageChange();
        ApplyLocalizedTexts();
        RefreshStoreRowVisibility();
    }

    /// <summary>
    /// 响应 ViewModel 的脚本放置行为变更请求，弹出确认对话框，根据用户选择提交或回滚变更。
    /// </summary>
    /// <param name="oldIndex">变更前的行为选项索引，用于回滚时恢复，须在 <see cref="ScriptPlacementBehavior"/> 枚举范围内。</param>
    /// <param name="newIndex">变更后的目标行为选项索引，须在 <see cref="ScriptPlacementBehavior"/> 枚举范围内。</param>
    /// <remarks>
    /// 取消语义：无 <see cref="System.Threading.CancellationToken"/>，对话框关闭即结束，不可取消。
    /// 线程/重入：async void 事件处理器，仅在 UI 线程调用；多次快速触发时对话框可能叠加显示。
    /// </remarks>
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
    /// 响应 ViewModel 的编辑器性能策略变更请求，弹出确认对话框，根据用户选择提交或回滚变更。
    /// </summary>
    /// <param name="oldIndex">变更前的策略索引，用于回滚，非负整数。</param>
    /// <param name="newIndex">变更后的目标策略索引，非负整数。</param>
    /// <remarks>
    /// 取消语义：无 <see cref="System.Threading.CancellationToken"/>，对话框关闭即结束，不可取消。
    /// 线程/重入：async void 事件处理器，仅在 UI 线程调用；用户确认后将触发编辑器内容清空标志置位。
    /// </remarks>
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
    /// 通过静态标志 <see cref="PendingEditorClear"/> 延迟通知编辑器页面在下次激活时清空全部内容。
    /// </summary>
    /// <remarks>
    /// 编辑器页面使用 NavigationCacheMode.Required，实例在导航后仍然存活。
    /// Settings 与 Editor 不同时作为 Frame.Content，无法直接访问编辑器实例，
    /// 故采用静态标志进行跨页面延迟通信；编辑器页面须在 OnNavigatedTo 时消费并复位该标志。
    /// 设计决策 DECISION-EDITOR-CLEAR-001：跨页面延迟清空机制。
    /// </remarks>
    private static void ClearEditorContent()
    {
        if (Application.Current is not App { MainWindow: MainWindow })
        {
            return;
        }

        PendingEditorClear = true;
    }

    /// <summary>
    /// 标识是否存在待处理的编辑器内容清空请求，由 <see cref="ClearEditorContent"/> 置为 <see langword="true"/>，
    /// 由编辑器页面在下次激活时消费并复位为 <see langword="false"/>；可变，非缓存，仅在 UI 线程上读写。
    /// </summary>
    internal static bool PendingEditorClear;

    #endregion

    #region 本地化文本

    /// <summary>
    /// 将页面所有 UI 控件的文本内容更新为当前语言的本地化字符串，并调用宽屏与窄屏布局的子更新方法。
    /// </summary>
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
        LlmSettingsLabel.Text = Text.Localize("大模型设置");
        LlmSettingsDescription.Text = Text.Localize("配置 LLM API 以生成脚本代码");
        LlmSettingsButton.Content = Text.Localize("打开");

        ApplyWideLocalizedTexts();
        ApplyNarrowAboutLocalizedTexts();
    }

    /// <summary>
    /// 更新宽屏布局下关于区域相关控件的本地化文本。
    /// </summary>
    private void ApplyWideLocalizedTexts()
    {
        WideStoreLink.Content = Text.Localize("微软商店");
        WideResetLink.Content = Text.Localize("重置所有设置");
    }

    /// <summary>
    /// 更新窄屏布局下关于区域所有控件的本地化文本。
    /// </summary>
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

    /// <summary>
    /// 根据 <see cref="SettingsViewModel.HasStoreUrl"/> 刷新商店相关行与链接控件的可见性。
    /// </summary>
    private void RefreshStoreRowVisibility()
    {
        Visibility storeVisibility = ViewModel.HasStoreUrl ? Visibility.Visible : Visibility.Collapsed;
        NarrowStoreRow.Visibility = storeVisibility;
        WideStoreLink.Visibility = storeVisibility;
    }

    #endregion

    #region 快捷键对话框

    /// <summary>
    /// 处理"查看快捷键"按钮点击事件，构建并异步显示快捷键列表对话框。
    /// </summary>
    /// <param name="sender">事件发送方，通常为触发点击的按钮控件，非空。</param>
    /// <param name="e">路由事件参数，非空。</param>
    /// <remarks>
    /// 取消语义：无 <see cref="System.Threading.CancellationToken"/>，对话框关闭即完成，不可取消。
    /// 线程/重入：async void 事件处理器，仅在 UI 线程调用。
    /// </remarks>
    private async void ShortcutsButton_Click(object sender, RoutedEventArgs e)
    {
        ContentDialog dialog = BuildShortcutsDialog();
        await dialog.ShowAsync();
    }

    /// <summary>
    /// 构建并返回包含所有快捷键说明行的快捷键 <see cref="ContentDialog"/>。
    /// </summary>
    /// <returns>已配置好内容、标题与关闭按钮的快捷键对话框实例。</returns>
    private ContentDialog BuildShortcutsDialog()
    {
        StackPanel panel = new() { Spacing = 8, MinWidth = 380 };

        AddShortcutRow(panel, "Ctrl+Enter", Text.Localize("执行代码"));
        AddShortcutRow(panel, "Ctrl+L", Text.Localize("大模型生成代码"));
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

    /// <summary>
    /// 向指定 <see cref="StackPanel"/> 追加一行包含快捷键与功能说明的双列 <see cref="Grid"/> 布局行。
    /// </summary>
    /// <param name="panel">目标容器面板，非空，新行将追加至其 Children 末尾。</param>
    /// <param name="shortcut">快捷键字符串（如 <c>Ctrl+Enter</c>），非空，以等宽字体展示。</param>
    /// <param name="description">对应功能的说明文本，非空，以次要文本颜色展示。</param>
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

    /// <summary>
    /// 处理"打开高级设置"按钮点击事件，构建并异步显示高级设置对话框。
    /// </summary>
    /// <param name="sender">事件发送方，通常为触发点击的按钮控件，非空。</param>
    /// <param name="e">路由事件参数，非空。</param>
    /// <remarks>
    /// 取消语义：无 <see cref="System.Threading.CancellationToken"/>，对话框关闭即完成，不可取消。
    /// 线程/重入：async void 事件处理器，仅在 UI 线程调用。
    /// </remarks>
    private async void AdvancedSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ContentDialog dialog = BuildAdvancedSettingsDialog();
        await dialog.ShowAsync();
    }

    /// <summary>
    /// 构建并返回包含临时文件前缀、置信度阈值与语言执行命令编辑控件的高级设置 <see cref="ContentDialog"/>。
    /// </summary>
    /// <returns>已配置好内容区域、保存与取消按钮及输入校验逻辑的高级设置对话框实例。</returns>
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

    /// <summary>
    /// 构建语言执行命令编辑面板，为每种支持的语言生成一个可编辑的命令输入框，返回面板与输入框字典的元组。
    /// </summary>
    /// <returns>
    /// 包含语言命令编辑 <see cref="StackPanel"/> 与以语言名为键、<see cref="TextBox"/> 为值的字典的元组。
    /// </returns>
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

    /// <summary>
    /// 构建高级设置对话框中的"重置为默认"超链接按钮，点击后将各输入控件恢复为系统默认值。
    /// </summary>
    /// <param name="prefixTextBox">临时文件名前缀输入框，非空，重置时更新其 <see cref="TextBox.Text"/>。</param>
    /// <param name="thresholdBox">置信度阈值数值输入框，非空，重置时更新其 <see cref="NumberBox.Value"/>。</param>
    /// <param name="commandTextBoxes">以语言名为键、命令输入框为值的字典，非空，重置时批量更新各 <see cref="TextBox.Text"/>。</param>
    /// <returns>已绑定点击重置事件的 <see cref="HyperlinkButton"/> 实例。</returns>
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

    /// <summary>
    /// 处理"重置所有设置"链接点击事件，弹出确认对话框并在用户确认后执行全量重置。
    /// </summary>
    /// <param name="sender">事件发送方，通常为超链接按钮控件，非空。</param>
    /// <param name="e">路由事件参数，非空。</param>
    /// <remarks>
    /// 取消语义：无 <see cref="System.Threading.CancellationToken"/>，对话框关闭即结束，不可取消。
    /// 线程/重入：async void 事件处理器，仅在 UI 线程调用；重置后重新应用主题与本地化文本，并刷新可见性。
    /// </remarks>
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

    #region LLM 设置对话框

    /// <summary>
    /// 处理"打开大模型设置"按钮点击事件，构建并异步显示大模型配置对话框。
    /// </summary>
    /// <param name="sender">事件发送方，通常为触发点击的按钮控件，非空。</param>
    /// <param name="e">路由事件参数，非空。</param>
    /// <remarks>
    /// 取消语义：无 <see cref="System.Threading.CancellationToken"/>，对话框关闭即完成，不可取消。
    /// 线程/重入：async void 事件处理器，仅在 UI 线程调用。
    /// </remarks>
    private async void LlmSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ContentDialog dialog = BuildLlmSettingsDialog();
        await dialog.ShowAsync();
    }

    /// <summary>
    /// 构建并返回包含 API Key、基础 URL、模型名称、最大 Token 数与请求超时配置控件的大模型设置 <see cref="ContentDialog"/>。
    /// </summary>
    /// <returns>已配置好内容区域、保存与取消按钮及配置写入逻辑的大模型设置对话框实例。</returns>
    private ContentDialog BuildLlmSettingsDialog()
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
            Title = Text.Localize("大模型设置"),
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

    /// <summary>
    /// 处理 GitHub 链接点击事件，使用系统默认浏览器打开项目 GitHub 主页。
    /// </summary>
    /// <param name="sender">事件发送方，通常为超链接按钮控件，非空。</param>
    /// <param name="e">路由事件参数，非空。</param>
    /// <remarks>
    /// 取消语义：无 <see cref="System.Threading.CancellationToken"/>，启动完成即结束，不可取消。
    /// 线程/重入：async void 事件处理器，仅在 UI 线程调用；URL 为空时静默跳过。
    /// </remarks>
    private async void GitHubLink_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(ViewModel.GitHubUrl))
        {
            await Launcher.LaunchUriAsync(new Uri(ViewModel.GitHubUrl));
        }
    }

    /// <summary>
    /// 处理商店链接点击事件，使用系统浏览器或商店客户端打开应用程序的商店页面。
    /// </summary>
    /// <param name="sender">事件发送方，通常为超链接按钮控件，非空。</param>
    /// <param name="e">路由事件参数，非空。</param>
    /// <remarks>
    /// 取消语义：无 <see cref="System.Threading.CancellationToken"/>，启动完成即结束，不可取消。
    /// 线程/重入：async void 事件处理器，仅在 UI 线程调用；仅在 <see cref="SettingsViewModel.HasStoreUrl"/> 为 <see langword="true"/> 时执行。
    /// </remarks>
    private async void StoreLink_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.HasStoreUrl)
        {
            await Launcher.LaunchUriAsync(new Uri(ViewModel.StoreUrl));
        }
    }

    #endregion
}