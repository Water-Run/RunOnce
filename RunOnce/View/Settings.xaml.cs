/*
 * 设置页面视图
 * 提供应用程序配置界面的 View 层实现，负责本地化文本、对话框展示与主题应用
 *
 * @author: WaterRun
 * @file: View/Settings.xaml.cs
 * @date: 2026-04-09
 */

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
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
    /// 标识是否存在待处理的编辑器内容清空请求，由 <see cref="ClearEditorContent"/> 置为 <see langword="true"/>，
    /// 由编辑器页面在下次激活时消费并复位为 <see langword="false"/>；可变，非缓存，仅在 UI 线程上读写。
    /// </summary>
    internal static bool PendingEditorClear;

    /// <summary>
    /// 标识是否存在待处理的滚动到 LLM 设置区域的请求。
    /// 由编辑器页面"前往设置"功能置为 <see langword="true"/>，
    /// 由本页面在 <see cref="HandlePageLoaded"/> 中消费并复位。
    /// </summary>
    internal static bool PendingScrollToLlm;

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
    /// 处理页面加载完成事件，依次执行本地化文本应用、商店行可见性刷新与待处理的滚动请求。
    /// </summary>
    /// <param name="sender">事件发送方，通常为当前页面实例，非空。</param>
    /// <param name="e">路由事件参数，非空。</param>
    private void HandlePageLoaded(object sender, RoutedEventArgs e)
    {
        ApplyLocalizedTexts();
        RefreshStoreRowVisibility();

        if (PendingScrollToLlm)
        {
            PendingScrollToLlm = false;
            LlmSectionHeader.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = true });
        }
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

    #endregion

    #region 本地化文本

    /// <summary>
    /// 将页面所有 UI 控件的文本内容更新为当前语言的本地化字符串，并调用各子区域的更新方法。
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
        AdvancedSettingsLabel.Text = Text.Localize("高级设置");
        AdvancedSettingsDescription.Text = Text.Localize("配置临时文件、置信度阈值和语言命令");
        AdvancedSettingsButton.Content = Text.Localize("打开");

        ApplyLlmLocalizedTexts();
        ApplyWideLocalizedTexts();
        ApplyNarrowAboutLocalizedTexts();
    }

    /// <summary>
    /// 更新 LLM 设置区域的本地化文本并从 Config 初始化输入控件值。
    /// </summary>
    private void ApplyLlmLocalizedTexts()
    {
        LlmSectionHeader.Text = Text.Localize("大模型");
        LlmApiKeyLabel.Text = Text.Localize("API Key");
        LlmBaseUrlLabel.Text = Text.Localize("API 基础 URL");
        LlmModelLabel.Text = Text.Localize("模型名称");
        LlmVerifyButton.Content = Text.Localize("生成可用检测");

        if (LlmClient.IsConnectionVerified)
        {
            SetLlmVerifyStatus(
                Text.Localize("检测通过"),
                new SolidColorBrush(Color.FromArgb(255, 16, 124, 16)));
        }
        else
        {
            LlmVerifyStatus.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
            LlmVerifyStatus.Text = Text.Localize("未检测");
        }

        LlmAdvancedLabel.Text = Text.Localize("高级设置");
        LlmAdvancedDescription.Text = Text.Localize("配置语言偏好、附加提示词与校对选项");
        LlmAdvancedButton.Content = Text.Localize("打开");
        LlmResetLink.Content = Text.Localize("重置大模型设置");

        LlmApiKeyBox.Password = Config.LlmApiKey;
        LlmBaseUrlBox.Text = Config.LlmBaseUrl;
        LlmBaseUrlBox.PlaceholderText = Config.DefaultLlmBaseUrl;
        LlmModelBox.Text = Config.LlmModel;
        LlmModelBox.PlaceholderText = Config.DefaultLlmModel;
    }

    /// <summary>
    /// 更新宽屏布局下关于区域相关控件的本地化文本。
    /// </summary>
    private void ApplyWideLocalizedTexts()
    {
        WideStoreLink.Content = Text.Localize("微软商店");
        WideShortcutsLink.Content = Text.Localize("查看快捷键");
        WideResetLink.Content = Text.Localize("重置所有设置");
        WideBuildTimeText.Text = $"{Text.Localize("编译于")} {ViewModel.BuildTimeText}";
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
        NarrowShortcutsLabel.Text = Text.Localize("快捷键");
        NarrowShortcutsLink.Content = Text.Localize("查看");
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
    /// 宽屏左侧面板的 <see cref="WideShortcutsLink"/> 与窄屏关于区域的 <see cref="NarrowShortcutsLink"/> 均绑定到此处理器。
    /// </summary>
    /// <param name="sender">事件发送方，通常为触发点击的超链接按钮控件，非空。</param>
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
        AddShortcutRow(panel, "Shift+Ctrl+Enter", Text.Localize("管理员运行"));
        AddShortcutRow(panel, "Ctrl+L", Text.Localize("大模型生成代码"));
        AddShortcutRow(panel, "Ctrl+S", Text.Localize("设置"));
        AddShortcutRow(panel, "Ctrl+E", Text.Localize("命令行参数"));
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
    /// 构建并返回包含临时文件前缀、置信度阈值、管理员运行方式与语言执行命令编辑控件的高级设置 <see cref="ContentDialog"/>。
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

        List<string> adminModeItems = Enum.GetValues<AdminRunMode>()
            .Select(Config.GetAdminRunModeDisplayName)
            .ToList();
        ComboBox adminModeBox = new()
        {
            Header = Text.Localize("管理员运行方式"),
            ItemsSource = adminModeItems,
            SelectedIndex = (int)Config.AdminMode,
            MinWidth = 200,
        };
        contentPanel.Children.Add(adminModeBox);

        (StackPanel commandsPanel, Dictionary<string, TextBox> commandTextBoxes) = BuildLanguageCommandControls();
        contentPanel.Children.Add(commandsPanel);

        HyperlinkButton resetLink = BuildResetAdvancedLink(prefixTextBox, thresholdBox, commandTextBoxes, adminModeBox);
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

                if (adminModeBox.SelectedIndex >= 0)
                {
                    Config.AdminMode = (AdminRunMode)adminModeBox.SelectedIndex;
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
    /// <param name="adminModeBox">管理员运行方式下拉框，非空，重置时恢复默认选项。</param>
    /// <returns>已绑定点击重置事件的 <see cref="HyperlinkButton"/> 实例。</returns>
    private HyperlinkButton BuildResetAdvancedLink(
        TextBox prefixTextBox,
        NumberBox thresholdBox,
        Dictionary<string, TextBox> commandTextBoxes,
        ComboBox adminModeBox)
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
            adminModeBox.SelectedIndex = (int)AdminRunMode.WindowsSudo;

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
        LlmClient.ResetVerificationState();

        if (Application.Current is App app)
        {
            app.ApplyTheme(Config.Theme);
        }

        ApplyLocalizedTexts();
        RefreshStoreRowVisibility();
    }

    #endregion

    #region LLM 设置

    /// <summary>
    /// 更新 LLM 验证状态指示灯文本与颜色。
    /// </summary>
    /// <param name="text">状态描述文本，非空。</param>
    /// <param name="foreground">指示灯与文本颜色画刷，非空。</param>
    private void SetLlmVerifyStatus(string text, Brush foreground)
    {
        LlmVerifyStatus.Text = $"●  {text}";
        LlmVerifyStatus.Foreground = foreground;
    }

    /// <summary>
    /// 处理 API Key 输入变更事件，立即持久化到 Config 并重置验证状态。
    /// </summary>
    /// <param name="sender">事件发送方，通常为 <see cref="PasswordBox"/> 控件，非空。</param>
    /// <param name="e">路由事件参数，非空。</param>
    private void LlmApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        Config.LlmApiKey = LlmApiKeyBox.Password;
        LlmClient.ResetVerificationState();
        LlmVerifyStatus.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        LlmVerifyStatus.Text = Text.Localize("未检测");
    }

    /// <summary>
    /// 处理 API 基础 URL 失焦事件，持久化到 Config 并重置验证状态。
    /// </summary>
    /// <param name="sender">事件发送方，通常为 <see cref="TextBox"/> 控件，非空。</param>
    /// <param name="e">路由事件参数，非空。</param>
    private void LlmBaseUrlBox_LostFocus(object sender, RoutedEventArgs e)
    {
        Config.LlmBaseUrl = string.IsNullOrWhiteSpace(LlmBaseUrlBox.Text)
            ? Config.DefaultLlmBaseUrl
            : LlmBaseUrlBox.Text;
        LlmClient.ResetVerificationState();
        LlmVerifyStatus.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        LlmVerifyStatus.Text = Text.Localize("未检测");
    }

    /// <summary>
    /// 处理模型名称失焦事件，持久化到 Config 并重置验证状态。
    /// </summary>
    /// <param name="sender">事件发送方，通常为 <see cref="TextBox"/> 控件，非空。</param>
    /// <param name="e">路由事件参数，非空。</param>
    private void LlmModelBox_LostFocus(object sender, RoutedEventArgs e)
    {
        Config.LlmModel = string.IsNullOrWhiteSpace(LlmModelBox.Text)
            ? Config.DefaultLlmModel
            : LlmModelBox.Text;
        LlmClient.ResetVerificationState();
        LlmVerifyStatus.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        LlmVerifyStatus.Text = Text.Localize("未检测");
    }

    /// <summary>
    /// 处理验证连接按钮点击事件，将当前输入持久化后异步验证 LLM API 连接可达性。
    /// </summary>
    /// <param name="sender">事件发送方，通常为触发点击的按钮控件，非空。</param>
    /// <param name="e">路由事件参数，非空。</param>
    /// <remarks>
    /// 取消语义：无 <see cref="System.Threading.CancellationToken"/>，验证完成或异常后即结束。
    /// 线程/重入：async void 事件处理器，仅在 UI 线程调用；验证期间禁用按钮防止重入。
    /// </remarks>
    private async void LlmVerifyButton_Click(object sender, RoutedEventArgs e)
    {
        Config.LlmApiKey = LlmApiKeyBox.Password;
        Config.LlmBaseUrl = string.IsNullOrWhiteSpace(LlmBaseUrlBox.Text)
            ? Config.DefaultLlmBaseUrl
            : LlmBaseUrlBox.Text;
        Config.LlmModel = string.IsNullOrWhiteSpace(LlmModelBox.Text)
            ? Config.DefaultLlmModel
            : LlmModelBox.Text;

        LlmVerifyButton.IsEnabled = false;
        LlmVerifyStatus.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        LlmVerifyStatus.Text = Text.Localize("正在检测...");

        try
        {
            bool ok = await LlmClient.VerifyConnectionAsync();
            if (ok)
            {
                SetLlmVerifyStatus(
                    Text.Localize("检测通过"),
                    new SolidColorBrush(Color.FromArgb(255, 16, 124, 16)));
            }
            else
            {
                LlmVerifyStatus.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
                LlmVerifyStatus.Text = Text.Localize("未检测");
            }
        }
        catch (Exception ex)
        {
            SetLlmVerifyStatus(
                Text.Localize("检测失败: {0}", ex.Message),
                new SolidColorBrush(Color.FromArgb(255, 196, 43, 28)));
        }
        finally
        {
            LlmVerifyButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// 处理 LLM 高级设置按钮点击事件，构建并异步显示 LLM 高级设置对话框。
    /// </summary>
    /// <param name="sender">事件发送方，通常为触发点击的按钮控件，非空。</param>
    /// <param name="e">路由事件参数，非空。</param>
    /// <remarks>
    /// 取消语义：无 <see cref="System.Threading.CancellationToken"/>，对话框关闭即完成，不可取消。
    /// 线程/重入：async void 事件处理器，仅在 UI 线程调用。
    /// </remarks>
    private async void LlmAdvancedButton_Click(object sender, RoutedEventArgs e)
    {
        ContentDialog dialog = BuildLlmAdvancedDialog();
        await dialog.ShowAsync();
    }

    /// <summary>
    /// 构建并返回包含语言偏好、附加提示词、二次校对、自动执行、最大 Token 数与超时配置的 LLM 高级设置 <see cref="ContentDialog"/>。
    /// </summary>
    /// <returns>已配置好内容区域、保存与取消按钮及配置写入逻辑的 LLM 高级设置对话框实例。</returns>
    private ContentDialog BuildLlmAdvancedDialog()
    {
        StackPanel panel = new() { Spacing = 16, MinWidth = 450, Margin = new Thickness(0, 0, 8, 0) };

        List<string> langItems = Config.SupportedLanguages.Select(l => l.ToUpperInvariant()).ToList();
        ComboBox langBox = new()
        {
            Header = Text.Localize("语言偏好"),
            ItemsSource = langItems,
            SelectedIndex = Math.Max(0, langItems.IndexOf(Config.LlmLanguagePreference.ToUpperInvariant())),
            MinWidth = 200,
        };
        panel.Children.Add(langBox);

        TextBox promptBox = new()
        {
            Header = Text.Localize("附加提示词"),
            Text = Config.LlmAdditionalPrompt,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 80,
            MaxHeight = 160,
        };
        panel.Children.Add(promptBox);

        ToggleSwitch doubleCheckSwitch = new()
        {
            Header = Text.Localize("生成后二次校对"),
            IsOn = Config.LlmDoubleCheck,
            OnContent = null,
            OffContent = null,
        };
        panel.Children.Add(doubleCheckSwitch);

        ToggleSwitch autoExecSwitch = new()
        {
            Header = Text.Localize("自动立即执行"),
            IsOn = Config.LlmAutoExecute,
            IsEnabled = Config.LlmDoubleCheck,
            OnContent = null,
            OffContent = null,
        };
        doubleCheckSwitch.Toggled += (_, _) =>
        {
            autoExecSwitch.IsEnabled = doubleCheckSwitch.IsOn;
            if (!doubleCheckSwitch.IsOn)
            {
                autoExecSwitch.IsOn = false;
            }
        };
        panel.Children.Add(autoExecSwitch);

        NumberBox maxTokensBox = new()
        {
            Header = Text.Localize("最大 Token 数"),
            Value = Config.LlmMaxTokens,
            Minimum = 256,
            Maximum = 65536,
            SmallChange = 1024,
            LargeChange = 4096,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        panel.Children.Add(maxTokensBox);

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
        panel.Children.Add(timeoutBox);

        ScrollViewer scrollViewer = new()
        {
            Content = panel,
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

        dialog.PrimaryButtonClick += (_, _) =>
        {
            if (langBox.SelectedIndex >= 0 && langBox.SelectedIndex < Config.SupportedLanguages.Count)
            {
                Config.LlmLanguagePreference = Config.SupportedLanguages[langBox.SelectedIndex];
            }

            Config.LlmAdditionalPrompt = promptBox.Text;
            Config.LlmDoubleCheck = doubleCheckSwitch.IsOn;
            Config.LlmAutoExecute = autoExecSwitch.IsOn;

            if (!double.IsNaN(maxTokensBox.Value) && maxTokensBox.Value > 0)
            {
                Config.LlmMaxTokens = (int)maxTokensBox.Value;
            }

            if (!double.IsNaN(timeoutBox.Value) && timeoutBox.Value > 0)
            {
                Config.LlmTimeoutSeconds = (int)timeoutBox.Value;
            }
        };

        return dialog;
    }

    /// <summary>
    /// 处理重置大模型设置链接点击事件，弹出确认对话框并在用户确认后重置所有 LLM 设置。
    /// </summary>
    /// <param name="sender">事件发送方，通常为超链接按钮控件，非空。</param>
    /// <param name="e">路由事件参数，非空。</param>
    /// <remarks>
    /// 取消语义：无 <see cref="System.Threading.CancellationToken"/>，对话框关闭即结束，不可取消。
    /// 线程/重入：async void 事件处理器，仅在 UI 线程调用；重置后刷新 LLM 区域本地化文本。
    /// </remarks>
    private async void LlmResetLink_Click(object sender, RoutedEventArgs e)
    {
        ContentDialog dialog = new()
        {
            Title = Text.Localize("重置大模型设置"),
            Content = Text.Localize("确定要将所有设置重置为默认值吗？此操作无法撤销。"),
            PrimaryButtonText = Text.Localize("重置"),
            CloseButtonText = Text.Localize("取消"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() is ContentDialogResult.Primary)
        {
            ViewModel.ResetLlmSettings();
            ApplyLlmLocalizedTexts();
        }
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