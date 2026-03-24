/*
 * 代码编辑器页面视图
 * 提供代码编辑、视窗语法高亮、渐进式语言检测、自定义右键菜单、命令行参数与脚本执行的 View 层实现
 *
 * @author: WaterRun
 * @file: View/Editor.xaml.cs
 * @date: 2026-03-19
 */

#nullable enable

using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using RunOnce.Static;
using RunOnce.ViewModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using TextGetOptions = Microsoft.UI.Text.TextGetOptions;

namespace RunOnce.View;

/// <summary>
/// 代码编辑器页面，提供代码编辑、视窗语法高亮、渐进式语言检测、自定义右键菜单、命令行参数与执行功能。
/// </summary>
/// <remarks>
/// 不变量：<see cref="ViewModel"/> 在构造时创建，生命周期与页面一致；
/// 语法高亮通过 80ms 去抖定时器异步应用，仅处理视窗内及上下缓冲区的文本，不阻塞用户输入；
/// 在鼠标拖动选区期间完全暂停高亮，鼠标释放后延迟一帧再补做；
/// 格式化操作前后保存并恢复 ScrollViewer 滚动位置，防止视口抖动；
/// 右键菜单已替换为仅包含代码编辑操作的自定义菜单，空编辑器下右键直接粘贴。
/// 线程安全：所有成员必须在 UI 线程访问。
/// 副作用：高亮操作修改 RichEditBox 的字符格式；执行操作创建临时文件并启动终端进程。
/// </remarks>
public sealed partial class Editor : Page
{
    /// <summary>
    /// 自动检测的语言选择标识，区别于实际语言标识符。
    /// </summary>
    private const string AutoDetectToken = "\0auto";

    /// <summary>
    /// 编辑器页面的 ViewModel 实例。
    /// </summary>
    public EditorViewModel ViewModel { get; }

    /// <summary>
    /// 高亮与检测的去抖定时器。
    /// </summary>
    private DispatcherQueueTimer? _updateTimer;

    /// <summary>
    /// 滚动事件的去抖定时器，滚动停止后重新应用视窗高亮。
    /// </summary>
    private DispatcherQueueTimer? _scrollTimer;

    /// <summary>
    /// 标识当前是否正在应用字符格式，防止 SelectionChanged/TextChanged 回调干扰。
    /// </summary>
    private bool _isApplyingFormatting;

    /// <summary>
    /// 标识当前是否正在执行流程中，防止重入。
    /// </summary>
    private bool _isExecuting;

    /// <summary>
    /// 标识当前是否展现限制对话框。
    /// </summary>
    private bool _isShowingLimitDialog;

    /// <summary>
    /// 标识当前是否处于鼠标拖动选区过程。
    /// </summary>
    private bool _isPointerSelecting;

    /// <summary>
    /// 标识是否在拖动结束后补做一次高亮。
    /// </summary>
    private bool _pendingHighlightAfterPointerRelease;

    /// <summary>
    /// 标识当前是否正在通过 AI 生成代码，防止重入。
    /// </summary>
    private bool _isAiGenerating;

    /// <summary>
    /// RichEditBox 内部 ScrollViewer 的缓存引用，在页面加载时获取。
    /// </summary>
    private ScrollViewer? _scrollViewer;

    /// <summary>
    /// 上次高亮应用的视窗范围起始位置，用于判断是否需要重新高亮。
    /// </summary>
    private int _lastHighlightRangeStart = -1;

    /// <summary>
    /// 上次高亮应用的视窗范围结束位置，用于判断是否需要重新高亮。
    /// </summary>
    private int _lastHighlightRangeEnd = -1;

    /// <summary>
    /// 初始化编辑器页面实例。
    /// </summary>
    public Editor()
    {
        ViewModel = new EditorViewModel();
        InitializeComponent();
        Loaded += OnPageLoaded;
        ActualThemeChanged += OnActualThemeChanged;
    }

    /// <summary>
    /// 处理页面加载完成事件。
    /// </summary>
    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        EnsureTimerInitialized();
        CacheScrollViewer();
        RegisterContextRequestedHandler();
        RegisterScrollViewerEvents();
        InitializeWorkingDirectory();
        UpdatePlaceholderText();
        UpdatePlaceholderVisibility();
        CodeEditor.Focus(FocusState.Programmatic);

        // 若以 AI 模式启动，自动打开 AI 生成对话框
        if (Application.Current is App { IsAiMode: true })
        {
            _ = HandleAiGenerateAsync();
        }
    }

    /// <summary>
    /// 处理实际主题变更事件，重新应用高亮以更新颜色。
    /// </summary>
    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        if (_isPointerSelecting)
        {
            _pendingHighlightAfterPointerRelease = true;
            return;
        }

        InvalidateHighlightCache();
        ApplyViewportHighlighting();
    }

    #region 初始化

    /// <summary>
    /// 确保去抖定时器已初始化。
    /// </summary>
    private void EnsureTimerInitialized()
    {
        if (_updateTimer is null)
        {
            _updateTimer = DispatcherQueue.CreateTimer();
            _updateTimer.Interval = TimeSpan.FromMilliseconds(80);
            _updateTimer.IsRepeating = false;
            _updateTimer.Tick += (_, _) => PerformDebouncedUpdate();
        }

        if (_scrollTimer is null)
        {
            _scrollTimer = DispatcherQueue.CreateTimer();
            _scrollTimer.Interval = TimeSpan.FromMilliseconds(50);
            _scrollTimer.IsRepeating = false;
            _scrollTimer.Tick += (_, _) => ApplyViewportHighlighting();
        }
    }

    /// <summary>
    /// 在 RichEditBox 的可视树中查找并缓存内部 ScrollViewer 引用。
    /// </summary>
    private void CacheScrollViewer()
    {
        _scrollViewer = FindDescendant<ScrollViewer>(CodeEditor);
    }

    /// <summary>
    /// 注册 ScrollViewer 的滚动事件以触发视窗高亮刷新。
    /// </summary>
    private void RegisterScrollViewerEvents()
    {
        if (_scrollViewer is not null)
        {
            _scrollViewer.ViewChanged += OnScrollViewerViewChanged;
        }
    }

    /// <summary>
    /// 处理 ScrollViewer 滚动事件，通过去抖定时器触发视窗高亮刷新。
    /// </summary>
    private void OnScrollViewerViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_isApplyingFormatting || _isPointerSelecting)
        {
            return;
        }

        _scrollTimer?.Stop();
        _scrollTimer?.Start();
    }

    /// <summary>
    /// 注册 ContextRequested 事件处理程序以替换默认右键菜单。
    /// </summary>
    private void RegisterContextRequestedHandler()
    {
        CodeEditor.AddHandler(
            UIElement.ContextRequestedEvent,
            new TypedEventHandler<UIElement, ContextRequestedEventArgs>(CodeEditor_ContextRequested),
            true);
    }

    /// <summary>
    /// 在可视树中递归查找指定类型的第一个后代元素。
    /// </summary>
    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T target)
            {
                return target;
            }

            T? result = FindDescendant<T>(child);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    /// <summary>
    /// 从启动参数或环境变量初始化工作目录。
    /// </summary>
    private void InitializeWorkingDirectory()
    {
        if (Application.Current is App app && !string.IsNullOrEmpty(app.LaunchArguments))
        {
            string candidate = app.LaunchArguments.Trim('"');
            if (Directory.Exists(candidate))
            {
                ViewModel.WorkingDirectory = candidate;
                return;
            }
        }

        string[] args = Environment.GetCommandLineArgs();
        if (args.Length > 1)
        {
            string candidate = args[1].Trim('"');
            if (Directory.Exists(candidate))
            {
                ViewModel.WorkingDirectory = candidate;
            }
        }
    }

    #endregion

    #region 自定义右键菜单

    /// <summary>
    /// 处理编辑器的右键菜单请求事件，替换默认的富文本格式菜单。
    /// </summary>
    private async void CodeEditor_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        args.Handled = true;

        string text = GetPlainText();
        if (string.IsNullOrEmpty(text))
        {
            // 编辑器为空时，优先提供粘贴与 AI 生成两种入口
            await PasteTextFromClipboardAsync();
            return;
        }

        ShowCodeContextMenu(args);
    }

    /// <summary>
    /// 从系统剪贴板获取纯文本并粘贴到编辑器中。
    /// </summary>
    private async Task PasteTextFromClipboardAsync()
    {
        try
        {
            DataPackageView content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Text))
            {
                string text = await content.GetTextAsync();
                if (!string.IsNullOrEmpty(text))
                {
                    string currentText = GetPlainText();
                    int maxLen = Config.MaxCodeLength;
                    int remaining = maxLen - currentText.Length;
                    if (remaining <= 0)
                    {
                        _ = ShowCharacterLimitDialogAsync();
                        return;
                    }

                    bool truncated = false;
                    if (text.Length > remaining)
                    {
                        text = text[..remaining];
                        truncated = true;
                    }

                    CodeEditor.Document.Selection.TypeText(text);

                    if (truncated)
                    {
                        _ = ShowCharacterLimitDialogAsync();
                    }
                }
            }
        }
        catch
        {
            // CLIP-001: 剪贴板访问失败时静默忽略，不影响编辑器正常使用
        }
    }

    /// <summary>
    /// 构建并显示自定义代码编辑右键菜单。
    /// </summary>
    private void ShowCodeContextMenu(ContextRequestedEventArgs args)
    {
        bool hasSelection = CodeEditor.Document.Selection.StartPosition != CodeEditor.Document.Selection.EndPosition;

        MenuFlyout flyout = new();

        MenuFlyoutItem aiItem = new()
        {
            Text = Text.Localize("AI 生成"),
            Icon = new FontIcon { FontFamily = new FontFamily("Segoe MDL2 Assets"), Glyph = "\xE82F" },
            KeyboardAcceleratorTextOverride = "Ctrl+G",
        };
        aiItem.Click += (_, _) => _ = HandleAiGenerateAsync();
        flyout.Items.Add(aiItem);

        flyout.Items.Add(new MenuFlyoutSeparator());

        MenuFlyoutItem redoItem = new()
        {
            Text = Text.Localize("重做"),
            Icon = new SymbolIcon(Symbol.Redo),
            KeyboardAcceleratorTextOverride = "Ctrl+Y",
        };
        redoItem.Click += (_, _) => CodeEditor.Document.Redo();
        flyout.Items.Add(redoItem);

        flyout.Items.Add(new MenuFlyoutSeparator());

        MenuFlyoutItem cutItem = new()
        {
            Text = Text.Localize("剪切"),
            Icon = new SymbolIcon(Symbol.Cut),
            KeyboardAcceleratorTextOverride = "Ctrl+X",
            IsEnabled = hasSelection,
        };
        cutItem.Click += (_, _) => CodeEditor.Document.Selection.Cut();
        flyout.Items.Add(cutItem);

        MenuFlyoutItem copyItem = new()
        {
            Text = Text.Localize("复制"),
            Icon = new SymbolIcon(Symbol.Copy),
            KeyboardAcceleratorTextOverride = "Ctrl+C",
            IsEnabled = hasSelection,
        };
        copyItem.Click += (_, _) => CodeEditor.Document.Selection.Copy();
        flyout.Items.Add(copyItem);

        MenuFlyoutItem pasteItem = new()
        {
            Text = Text.Localize("粘贴"),
            Icon = new SymbolIcon(Symbol.Paste),
            KeyboardAcceleratorTextOverride = "Ctrl+V",
        };
        pasteItem.Click += (_, _) => CodeEditor.Document.Selection.Paste(0);
        flyout.Items.Add(pasteItem);

        flyout.Items.Add(new MenuFlyoutSeparator());

        MenuFlyoutItem selectAllItem = new()
        {
            Text = Text.Localize("全选"),
            KeyboardAcceleratorTextOverride = "Ctrl+A",
        };
        selectAllItem.Click += (_, _) =>
        {
            string t = GetPlainText();
            CodeEditor.Document.Selection.SetRange(0, t.Length);
        };
        flyout.Items.Add(selectAllItem);

        if (args.TryGetPosition(CodeEditor, out Point point))
        {
            flyout.ShowAt(CodeEditor, new FlyoutShowOptions { Position = point });
        }
        else
        {
            flyout.ShowAt(CodeEditor);
        }
    }

    #endregion

    #region 指针拖动选区保护

    /// <summary>
    /// 处理编辑器指针按下事件，进入拖动选区保护状态。
    /// </summary>
    private void CodeEditor_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(CodeEditor);
        if (point.Properties.IsLeftButtonPressed)
        {
            _isPointerSelecting = true;
            _pendingHighlightAfterPointerRelease = false;
        }
    }

    /// <summary>
    /// 处理编辑器指针释放事件，退出拖动选区保护状态。
    /// </summary>
    private void CodeEditor_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EndPointerSelectionAndScheduleHighlight();
    }

    /// <summary>
    /// 处理编辑器指针取消事件，退出拖动选区保护状态。
    /// </summary>
    private void CodeEditor_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        EndPointerSelectionAndScheduleHighlight();
    }

    /// <summary>
    /// 处理编辑器指针捕获丢失事件，退出拖动选区保护状态。
    /// </summary>
    private void CodeEditor_PointerCaptureLost(object sender, RoutedEventArgs e)
    {
        EndPointerSelectionAndScheduleHighlight();
    }

    /// <summary>
    /// 处理编辑器失焦事件，退出拖动选区保护状态。
    /// </summary>
    private void CodeEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        EndPointerSelectionAndScheduleHighlight();
    }

    /// <summary>
    /// 结束拖动选区状态，通过 DispatcherQueue 延迟一帧后补做高亮，
    /// 确保选区状态已完全稳定。
    /// </summary>
    private void EndPointerSelectionAndScheduleHighlight()
    {
        if (!_isPointerSelecting)
        {
            return;
        }

        _isPointerSelecting = false;

        if (_pendingHighlightAfterPointerRelease)
        {
            _pendingHighlightAfterPointerRelease = false;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (!_isPointerSelecting)
                {
                    ApplyViewportHighlighting();
                }
            });
        }
    }

    #endregion

    #region 文本与光标事件

    /// <summary>
    /// 处理代码文本变更事件，启动去抖定时器。
    /// </summary>
    private void CodeEditor_TextChanged(object sender, RoutedEventArgs e)
    {
        if (_isApplyingFormatting)
        {
            return;
        }

        UpdatePlaceholderVisibility();

        string text = GetPlainText();
        int maxLen = Config.MaxCodeLength;
        if (text.Length > maxLen)
        {
            EnforceCharacterLimit(text, maxLen);
        }

        InvalidateHighlightCache();

        _updateTimer?.Stop();
        _updateTimer?.Start();
    }

    /// <summary>
    /// 字符数量限制。
    /// </summary>
    private void EnforceCharacterLimit(string text, int maxLen)
    {
        _isApplyingFormatting = true;
        try
        {
            var range = CodeEditor.Document.GetRange(maxLen, text.Length);
            range.Text = string.Empty;
            CodeEditor.Document.Selection.SetRange(maxLen, maxLen);
        }
        finally
        {
            _isApplyingFormatting = false;
        }

        _ = ShowCharacterLimitDialogAsync();
    }

    /// <summary>
    /// 显示字符数限制对话框。
    /// </summary>
    private async Task ShowCharacterLimitDialogAsync()
    {
        if (_isShowingLimitDialog || XamlRoot is null)
        {
            return;
        }

        _isShowingLimitDialog = true;
        try
        {
            string message = Text.Localize("你的内容超过最大长度限制, 超出部分已被截断.")
                + "\n\n"
                + Text.Localize("\"一次运行\"面向的是\"一次性\"的脚本, 当代码达到这个长度时, 你最好需要好好看看, 而不是直接粘贴运行.");

            ContentDialog dialog = new()
            {
                Title = Text.Localize("超出输入限制"),
                Content = message,
                CloseButtonText = Text.Localize("确定"),
                XamlRoot = XamlRoot,
            };
            await dialog.ShowAsync();
        }
        finally
        {
            _isShowingLimitDialog = false;
        }
    }

    /// <summary>
    /// 处理光标位置变更事件，更新 ViewModel 中的行列信息。
    /// </summary>
    private void CodeEditor_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_isApplyingFormatting)
        {
            return;
        }

        string text = GetPlainText();
        int position = CodeEditor.Document.Selection.StartPosition;
        ViewModel.UpdateCursorPosition(text, position);
    }

    /// <summary>
    /// 去抖定时器回调，执行语言检测与语法高亮。
    /// </summary>
    private void PerformDebouncedUpdate()
    {
        string text = GetPlainText();
        ViewModel.RunDetection(NormalizeForAnalysis(text));

        if (_isPointerSelecting)
        {
            _pendingHighlightAfterPointerRelease = true;
            return;
        }

        ApplyViewportHighlighting(text);
    }

    #endregion

    #region 键盘处理

    /// <summary>
    /// 处理编辑器的按键预览事件，拦截 Ctrl+Enter、Ctrl+E、Ctrl+B/I/U 和 Tab 键。
    /// </summary>
    private void CodeEditor_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        CoreVirtualKeyStates ctrlState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        bool isCtrlDown = (ctrlState & CoreVirtualKeyStates.Down) != 0;

        if (isCtrlDown)
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                HandleExecuteRequest();
                return;
            }

            if (e.Key == VirtualKey.E)
            {
                e.Handled = true;
                _ = HandleArgsRequestAsync();
                return;
            }

            if (e.Key == VirtualKey.G)
            {
                e.Handled = true;
                _ = HandleAiGenerateAsync();
                return;
            }

            if (e.Key is VirtualKey.B or VirtualKey.I or VirtualKey.U)
            {
                e.Handled = true;
                return;
            }
        }

        if (e.Key == VirtualKey.Tab)
        {
            e.Handled = true;

            CoreVirtualKeyStates shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
            bool isShift = (shiftState & CoreVirtualKeyStates.Down) != 0;

            if (isShift)
            {
                RemoveLeadingIndent();
            }
            else
            {
                CodeEditor.Document.Selection.TypeText("    ");
            }
        }
    }

    /// <summary>
    /// 处理 Ctrl+Enter 快捷键（Page 级后备）。
    /// </summary>
    private void ExecuteAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        HandleExecuteRequest();
    }

    /// <summary>
    /// 处理 Ctrl+E 快捷键（Page 级后备）。
    /// </summary>
    private void ArgsAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = HandleArgsRequestAsync();
    }

    /// <summary>
    /// 处理 Ctrl+G 快捷键（Page 级后备）。
    /// </summary>
    private void AiGenerateAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = HandleAiGenerateAsync();
    }

    /// <summary>
    /// 删除当前行开头的至多 4 个空格。
    /// </summary>
    private void RemoveLeadingIndent()
    {
        string text = GetPlainText();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        int cursorPos = CodeEditor.Document.Selection.StartPosition;
        int lineStart = cursorPos;
        while (lineStart > 0 && text[lineStart - 1] != '\r')
        {
            lineStart--;
        }

        int spacesToRemove = 0;
        for (int i = lineStart; i < text.Length && i < lineStart + 4 && text[i] == ' '; i++)
        {
            spacesToRemove++;
        }

        if (spacesToRemove > 0)
        {
            var range = CodeEditor.Document.GetRange(lineStart, lineStart + spacesToRemove);
            range.Text = string.Empty;
        }
    }

    #endregion

    #region 视窗语法高亮

    /// <summary>
    /// 使上次高亮缓存失效，强制下次高亮重新计算。
    /// </summary>
    private void InvalidateHighlightCache()
    {
        _lastHighlightRangeStart = -1;
        _lastHighlightRangeEnd = -1;
    }

    /// <summary>
    /// 计算当前视窗可见的字符范围（近似值）。
    /// </summary>
    /// <param name="text">编辑器当前的纯文本内容。</param>
    /// <returns>视窗范围的起始和结束字符索引元组。</returns>
    private (int Start, int End) EstimateVisibleCharRange(string text)
    {
        if (_scrollViewer is null || string.IsNullOrEmpty(text))
        {
            return (0, text?.Length ?? 0);
        }

        double verticalOffset = _scrollViewer.VerticalOffset;
        double viewportHeight = _scrollViewer.ViewportHeight;
        double extentHeight = _scrollViewer.ExtentHeight;

        if (extentHeight <= 0)
        {
            return (0, text.Length);
        }

        double topRatio = verticalOffset / extentHeight;
        double bottomRatio = (verticalOffset + viewportHeight) / extentHeight;

        int estimatedStart = Math.Max(0, (int)(topRatio * text.Length));
        int estimatedEnd = Math.Min(text.Length, (int)(bottomRatio * text.Length));

        return (estimatedStart, estimatedEnd);
    }

    /// <summary>
    /// 对编辑器内容应用视窗感知的语法高亮着色。
    /// </summary>
    /// <param name="text">编辑器原始文本，若为 null 则从编辑器获取。</param>
    private void ApplyViewportHighlighting(string? text = null)
    {
        if (_isPointerSelecting)
        {
            _pendingHighlightAfterPointerRelease = true;
            return;
        }

        text ??= GetPlainText();
        string language = ViewModel.EffectiveLanguage;

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        int buffer = Config.HighlightViewportBuffer;
        (int visibleStart, int visibleEnd) = EstimateVisibleCharRange(text);

        int rangeStart = Math.Max(0, visibleStart - buffer);
        int rangeEnd = Math.Min(text.Length, visibleEnd + buffer);

        if (rangeStart == _lastHighlightRangeStart && rangeEnd == _lastHighlightRangeEnd)
        {
            return;
        }

        _lastHighlightRangeStart = rangeStart;
        _lastHighlightRangeEnd = rangeEnd;

        double? savedVerticalOffset = _scrollViewer?.VerticalOffset;
        double? savedHorizontalOffset = _scrollViewer?.HorizontalOffset;

        _isApplyingFormatting = true;

        try
        {
            var doc = CodeEditor.Document;

            int selStart = doc.Selection.StartPosition;
            int selEnd = doc.Selection.EndPosition;

            var visibleRange = doc.GetRange(rangeStart, rangeEnd);
            visibleRange.CharacterFormat.ForegroundColor = GetDefaultTextColor();

            if (!string.IsNullOrEmpty(language))
            {
                string normalizedText = NormalizeForAnalysis(text);
                IReadOnlyList<HighlightSpan> spans = Highlight.Analyze(normalizedText, language, rangeStart, rangeEnd);

                foreach (HighlightSpan span in spans)
                {
                    int clampedStart = Math.Max(span.Start, rangeStart);
                    int clampedEnd = Math.Min(span.End, rangeEnd);

                    if (clampedStart < clampedEnd && clampedStart >= 0 && clampedEnd <= text.Length)
                    {
                        var range = doc.GetRange(clampedStart, clampedEnd);
                        range.CharacterFormat.ForegroundColor = GetColorForToken(span.Type);
                    }
                }
            }

            doc.Selection.SetRange(selStart, selEnd);
        }
        finally
        {
            _isApplyingFormatting = false;
        }

        if (savedVerticalOffset.HasValue && savedHorizontalOffset.HasValue)
        {
            _scrollViewer?.ChangeView(savedHorizontalOffset.Value, savedVerticalOffset.Value, null, disableAnimation: true);
        }
    }

    /// <summary>
    /// 获取当前主题下的默认文本颜色。
    /// </summary>
    private Color GetDefaultTextColor()
    {
        return ActualTheme == ElementTheme.Dark
            ? Color.FromArgb(255, 204, 204, 204)
            : Color.FromArgb(255, 30, 30, 30);
    }

    /// <summary>
    /// 获取指定 Token 类型在当前主题下的着色。
    /// </summary>
    private Color GetColorForToken(TokenType type)
    {
        bool dark = ActualTheme == ElementTheme.Dark;
        return type switch
        {
            TokenType.Keyword => dark ? Color.FromArgb(255, 86, 156, 214) : Color.FromArgb(255, 0, 0, 255),
            TokenType.String => dark ? Color.FromArgb(255, 206, 145, 120) : Color.FromArgb(255, 163, 21, 21),
            TokenType.Comment => dark ? Color.FromArgb(255, 106, 153, 85) : Color.FromArgb(255, 0, 128, 0),
            TokenType.Number => dark ? Color.FromArgb(255, 181, 206, 168) : Color.FromArgb(255, 9, 134, 88),
            _ => GetDefaultTextColor(),
        };
    }

    #endregion

    #region 命令行参数

    /// <summary>
    /// 显示命令行参数输入对话框。
    /// </summary>
    public async Task HandleArgsRequestAsync()
    {
        if (XamlRoot is null)
        {
            return;
        }

        TextBox argsTextBox = new()
        {
            Text = ViewModel.CommandLineArguments,
            PlaceholderText = "e.g. --input data.txt --verbose",
            AcceptsReturn = false,
            MinWidth = 400,
        };

        ContentDialog dialog = new()
        {
            Title = Text.Localize("命令行参数"),
            Content = argsTextBox,
            PrimaryButtonText = Text.Localize("确定"),
            SecondaryButtonText = Text.Localize("清除"),
            CloseButtonText = Text.Localize("取消"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        bool confirmedViaEnter = false;
        argsTextBox.PreviewKeyDown += (_, keyArgs) =>
        {
            if (keyArgs.Key == VirtualKey.Enter)
            {
                keyArgs.Handled = true;
                confirmedViaEnter = true;
                dialog.Hide();
            }
        };

        ContentDialogResult result = await dialog.ShowAsync();

        if (result is ContentDialogResult.Primary || confirmedViaEnter)
        {
            ViewModel.CommandLineArguments = argsTextBox.Text;
        }
        else if (result is ContentDialogResult.Secondary)
        {
            ViewModel.CommandLineArguments = string.Empty;
        }

        NotifyMainWindowArgsDotChanged();
    }

    /// <summary>
    /// 通知 MainWindow 更新命令行参数指示点的可见性。
    /// </summary>
    private void NotifyMainWindowArgsDotChanged()
    {
        if (Application.Current is App { MainWindow: MainWindow mw })
        {
            mw.UpdateArgsDotVisibility(ViewModel.HasCommandLineArguments);
        }
    }

    #endregion

    #region AI 生成代码

    /// <summary>
    /// 显示 AI 代码生成对话框，允许用户输入需求描述，调用 LLM 生成并加载脚本代码。
    /// </summary>
    public async Task HandleAiGenerateAsync()
    {
        if (_isAiGenerating || XamlRoot is null)
        {
            return;
        }

        _isAiGenerating = true;

        try
        {
            await ShowAiGenerateDialogAsync();
        }
        finally
        {
            _isAiGenerating = false;
        }
    }

    /// <summary>
    /// 构建并显示 AI 生成对话框，执行生成流程。
    /// </summary>
    private async Task ShowAiGenerateDialogAsync()
    {
        // ── 构建对话框内容 ──
        StackPanel contentPanel = new() { Spacing = 12, MinWidth = 480 };

        TextBox promptBox = new()
        {
            Header = Text.Localize("描述你的需求"),
            PlaceholderText = Text.Localize("例如：列出当前目录下所有.txt文件"),
            AcceptsReturn = false,
        };
        contentPanel.Children.Add(promptBox);

        // 语言选择行
        StackPanel langRow = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };

        langRow.Children.Add(new TextBlock
        {
            Text = Text.Localize("语言"),
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
        });

        var langOptions = new List<string> { Text.Localize("自动") };
        langOptions.AddRange(Config.SupportedLanguages.Select(l => l.ToUpperInvariant()));

        ComboBox langBox = new()
        {
            ItemsSource = langOptions,
            SelectedIndex = 0,
            MinWidth = 130,
            VerticalAlignment = VerticalAlignment.Center,
        };
        langRow.Children.Add(langBox);
        contentPanel.Children.Add(langRow);

        // 进度环（初始隐藏）
        StackPanel progressRow = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Visibility = Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Center,
        };
        progressRow.Children.Add(new ProgressRing { IsActive = true, Width = 20, Height = 20 });
        progressRow.Children.Add(new TextBlock
        {
            Text = Text.Localize("正在生成..."),
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        contentPanel.Children.Add(progressRow);

        // 错误提示（初始隐藏）
        TextBlock errorText = new()
        {
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 196, 43, 28)),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        contentPanel.Children.Add(errorText);

        // ── 构建对话框 ──
        ContentDialog dialog = new()
        {
            Title = Text.Localize("AI 生成代码"),
            Content = contentPanel,
            PrimaryButtonText = Text.Localize("生成"),
            CloseButtonText = Text.Localize("取消"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        string? generatedCode = null;
        CancellationTokenSource? cts = null;

        // ── 生成按钮点击处理（保持对话框打开，异步执行）──
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            string prompt = promptBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(prompt))
            {
                args.Cancel = true;
                return;
            }

            // 防止对话框关闭，切换为加载状态
            args.Cancel = true;
            cts?.Dispose();
            cts = new CancellationTokenSource();
            await RunGenerationAsync(
                dialog, promptBox, langBox, progressRow, errorText,
                code => { generatedCode = code; },
                cts);
        };

        // 取消按钮点击时中止正在进行的请求
        dialog.CloseButtonClick += (_, _) => cts?.Cancel();

        // Enter 键快速提交（与点击"生成"按钮等效）
        promptBox.KeyDown += (_, keyArgs) =>
        {
            if (keyArgs.Key == VirtualKey.Enter)
            {
                keyArgs.Handled = true;
                string prompt = promptBox.Text.Trim();
                if (!string.IsNullOrWhiteSpace(prompt) && dialog.IsPrimaryButtonEnabled)
                {
                    cts?.Dispose();
                    cts = new CancellationTokenSource();
                    _ = RunGenerationAsync(
                        dialog, promptBox, langBox, progressRow, errorText,
                        code => { generatedCode = code; },
                        cts);
                }
            }
        };

        await dialog.ShowAsync();

        // ── 释放 CTS 并将生成结果加载到编辑器 ──
        cts?.Dispose();
        cts = null;

        if (!string.IsNullOrEmpty(generatedCode))
        {
            LoadCodeIntoEditor(generatedCode);
        }
    }

    /// <summary>
    /// 将生成的代码加载到编辑器，并触发语言检测与高亮。
    /// </summary>
    private void LoadCodeIntoEditor(string code)
    {
        _isApplyingFormatting = true;
        try
        {
            CodeEditor.Document.SetText(Microsoft.UI.Text.TextSetOptions.None, code);
        }
        finally
        {
            _isApplyingFormatting = false;
        }

        string normalizedCode = NormalizeForAnalysis(code);
        ViewModel.RunDetection(normalizedCode);
        ViewModel.ManualLanguage = null;

        InvalidateHighlightCache();
        ApplyViewportHighlighting(code);
        UpdatePlaceholderVisibility();

        CodeEditor.Document.Selection.SetRange(0, 0);
        CodeEditor.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// 执行 AI 生成的核心逻辑：调用 LlmClient，更新对话框 UI 状态，成功后关闭对话框。
    /// </summary>
    /// <remarks>
    /// 注意：调用方负责 cts 的释放，本方法不释放 cts。
    /// </remarks>
    private static async Task RunGenerationAsync(
        ContentDialog dialog,
        TextBox promptBox,
        ComboBox langBox,
        StackPanel progressRow,
        TextBlock errorText,
        Action<string> onSuccess,
        CancellationTokenSource? cts = null)
    {
        dialog.IsPrimaryButtonEnabled = false;
        promptBox.IsEnabled = false;
        langBox.IsEnabled = false;
        progressRow.Visibility = Visibility.Visible;
        errorText.Visibility = Visibility.Collapsed;

        string? preferredLanguage = langBox.SelectedIndex > 0
            ? Config.SupportedLanguages[langBox.SelectedIndex - 1]
            : null;

        try
        {
            CancellationToken token = cts?.Token ?? CancellationToken.None;
            string code = await LlmClient.GenerateScriptAsync(promptBox.Text.Trim(), preferredLanguage, token);
            onSuccess(code);
            dialog.Hide();
        }
        catch (OperationCanceledException)
        {
            // 用户取消，静默处理
        }
        catch (Exception ex)
        {
            progressRow.Visibility = Visibility.Collapsed;
            errorText.Text = ex.Message;
            errorText.Visibility = Visibility.Visible;
            dialog.IsPrimaryButtonEnabled = true;
            promptBox.IsEnabled = true;
            langBox.IsEnabled = true;
        }
    }

    #endregion

    #region 执行流程

    /// <summary>
    /// 处理执行请求。由 MainWindow 的运行按钮和 Ctrl+Enter 调用。
    /// </summary>
    public async void HandleExecuteRequest()
    {
        if (_isExecuting || XamlRoot is null)
        {
            return;
        }

        string code = GetPlainText();
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        _isExecuting = true;

        try
        {
            string? language = await DetermineExecutionLanguageAsync();
            if (string.IsNullOrEmpty(language))
            {
                return;
            }

            if (Config.ConfirmBeforeExecution)
            {
                ContentDialog confirmDialog = new()
                {
                    Title = Text.Localize("执行"),
                    Content = Text.Localize("确定要执行此代码吗？"),
                    PrimaryButtonText = Text.Localize("执行"),
                    CloseButtonText = Text.Localize("取消"),
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = XamlRoot,
                };

                confirmDialog.KeyDown += (_, args) =>
                {
                    if (args.Key == VirtualKey.Back)
                    {
                        args.Handled = true;
                        confirmDialog.Hide();
                    }
                };

                if (await confirmDialog.ShowAsync() is not ContentDialogResult.Primary)
                {
                    return;
                }
            }

            try
            {
                ViewModel.Execute(code, language);
            }
            catch (Exception ex)
            {
                ContentDialog errorDialog = new()
                {
                    Title = Text.Localize("执行"),
                    Content = ex.Message,
                    CloseButtonText = Text.Localize("确定"),
                    XamlRoot = XamlRoot,
                };
                await errorDialog.ShowAsync();
                return;
            }

            if (Config.AutoExitOnExecution)
            {
                Application.Current.Exit();
            }
        }
        finally
        {
            _isExecuting = false;
        }
    }

    /// <summary>
    /// 根据配置确定执行使用的语言，必要时弹出语言选择对话框。
    /// </summary>
    private async Task<string?> DetermineExecutionLanguageAsync()
    {
        if (ViewModel.ShouldShowLanguageSelector)
        {
            return await ShowLanguageSelectionDialogAsync(includeAutoDetect: false);
        }

        string language = ViewModel.EffectiveLanguage;
        if (!string.IsNullOrEmpty(language))
        {
            return language;
        }

        return await ShowLanguageSelectionDialogAsync(includeAutoDetect: false);
    }

    #endregion

    #region 语言选择对话框

    /// <summary>
    /// 处理状态栏语言指示器按钮点击事件。
    /// </summary>
    private async void LanguageButton_Click(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is null)
        {
            return;
        }

        string? selected = await ShowLanguageSelectionDialogAsync(includeAutoDetect: true);
        if (selected is null)
        {
            return;
        }

        if (selected == AutoDetectToken)
        {
            ViewModel.ManualLanguage = null;
        }
        else
        {
            ViewModel.ManualLanguage = selected;
        }

        InvalidateHighlightCache();
        ApplyViewportHighlighting();
    }

    /// <summary>
    /// 显示语言选择对话框。
    /// </summary>
    private async Task<string?> ShowLanguageSelectionDialogAsync(bool includeAutoDetect)
    {
        IReadOnlyList<DetectionResult> results = ViewModel.DetectionResults;

        List<(string Language, double Confidence)> sortedItems = results
            .Where(r => r.Confidence > 0)
            .OrderByDescending(r => r.Confidence)
            .Select(r => (r.Language, r.Confidence))
            .ToList();

        foreach (string lang in Config.SupportedLanguages)
        {
            if (sortedItems.All(i => !string.Equals(i.Language, lang, StringComparison.OrdinalIgnoreCase)))
            {
                sortedItems.Add((lang, 0));
            }
        }

        List<string> languageMap = [];
        ListView listView = new()
        {
            SelectionMode = ListViewSelectionMode.Single,
            MinWidth = 350,
        };

        if (includeAutoDetect)
        {
            languageMap.Add(AutoDetectToken);
            Grid autoGrid = new() { Padding = new Thickness(2, 6, 2, 6) };
            autoGrid.Children.Add(new TextBlock
            {
                Text = Text.Localize("自动检测"),
                FontStyle = Windows.UI.Text.FontStyle.Italic,
            });
            listView.Items.Add(autoGrid);
        }

        foreach ((string language, double confidence) in sortedItems)
        {
            languageMap.Add(language);

            Grid itemGrid = new() { Padding = new Thickness(2, 6, 2, 6) };
            itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock nameBlock = new()
            {
                Text = language.ToUpperInvariant(),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(nameBlock, 0);
            itemGrid.Children.Add(nameBlock);

            if (confidence > 0)
            {
                TextBlock confBlock = new()
                {
                    Text = confidence.ToString("P0"),
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(16, 0, 0, 0),
                };
                Grid.SetColumn(confBlock, 1);
                itemGrid.Children.Add(confBlock);
            }

            listView.Items.Add(itemGrid);
        }

        int preSelectIndex = 0;
        if (includeAutoDetect && ViewModel.ManualLanguage is not null)
        {
            int langIndex = languageMap.IndexOf(ViewModel.ManualLanguage);
            if (langIndex >= 0)
            {
                preSelectIndex = langIndex;
            }
        }

        listView.SelectedIndex = preSelectIndex;

        ContentDialog dialog = new()
        {
            Title = Text.Localize("选择语言"),
            Content = listView,
            PrimaryButtonText = Text.Localize("确定"),
            CloseButtonText = Text.Localize("取消"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        bool confirmedViaEnter = false;
        listView.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Enter)
            {
                args.Handled = true;
                confirmedViaEnter = true;
                dialog.Hide();
            }
        };

        dialog.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Back)
            {
                args.Handled = true;
                dialog.Hide();
            }
        };

        ContentDialogResult result = await dialog.ShowAsync();

        if (result is ContentDialogResult.Primary || confirmedViaEnter)
        {
            int selectedIndex = listView.SelectedIndex;
            if (selectedIndex >= 0 && selectedIndex < languageMap.Count)
            {
                return languageMap[selectedIndex];
            }
        }

        return null;
    }

    #endregion

    #region 公开方法：清空编辑器内容

    /// <summary>
    /// 清空编辑器中的所有文本内容并重置相关状态。
    /// </summary>
    /// <remarks>
    /// 由设置页面切换编辑器性能策略时调用。
    /// 清空操作将重置文本、高亮缓存、检测结果和光标位置。
    /// </remarks>
    public void ClearAllContent()
    {
        _isApplyingFormatting = true;
        try
        {
            CodeEditor.Document.SetText(Microsoft.UI.Text.TextSetOptions.None, string.Empty);
        }
        finally
        {
            _isApplyingFormatting = false;
        }

        InvalidateHighlightCache();
        ViewModel.RunDetection(string.Empty);
        ViewModel.ManualLanguage = null;
        ViewModel.UpdateCursorPosition(string.Empty, 0);
        UpdatePlaceholderVisibility();
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 从 RichEditBox 获取纯文本内容，去除尾部自动追加的 \r。
    /// </summary>
    private string GetPlainText()
    {
        CodeEditor.Document.GetText(TextGetOptions.None, out string text);

        if (text.Length > 0 && text[^1] == '\r')
        {
            text = text[..^1];
        }

        return text;
    }

    /// <summary>
    /// 将 RichEditBox 文本规范化为 \n 换行，供 Highlight.Analyze 和 LanguageDetector 使用。
    /// </summary>
    private static string NormalizeForAnalysis(string text)
    {
        return text.Replace('\r', '\n');
    }

    /// <summary>
    /// 根据编辑器是否有内容更新占位提示的可见性。
    /// </summary>
    private void UpdatePlaceholderVisibility()
    {
        string text = GetPlainText();
        PlaceholderPanel.Visibility = string.IsNullOrEmpty(text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>
    /// 更新占位提示的本地化文本。
    /// </summary>
    private void UpdatePlaceholderText()
    {
        PlaceholderText.Text = Text.Localize("在此粘贴代码");
        PlaceholderSubText.Text = Text.Localize("右键或Ctrl+V");
    }

    /// <summary>
    /// 刷新本地化文本，供 MainWindow 在语言切换后调用。
    /// </summary>
    public void RefreshLocalizedTexts()
    {
        ViewModel.RefreshLocalizedTexts();
        UpdatePlaceholderText();
    }

    #endregion
}