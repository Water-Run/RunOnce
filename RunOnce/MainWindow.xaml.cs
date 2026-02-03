/*
 * 应用程序主窗口
 * 提供沉浸式标题栏、页面导航框架及窗口尺寸管理
 * 
 * @author: WaterRun
 * @file: MainWindow.xaml.cs
 * @date: 2026-02-03
 */

#nullable enable

using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using RunOnce.Static;
using RunOnce.View;
using WinRT;

namespace RunOnce;

/// <summary>
/// 应用程序主窗口类，承载页面导航与沉浸式标题栏。
/// </summary>
/// <remarks>
/// 不变量：窗口创建后必须导航到编辑器页面；标题栏区域已注册为可拖拽区域。
/// 线程安全：所有成员必须在 UI 线程访问。
/// 副作用：构造时设置窗口尺寸、配置标题栏、执行初始导航。
/// </remarks>
public sealed partial class MainWindow : Window
{
    /// <summary>
    /// 窗口最小宽度（像素）。
    /// </summary>
    private const int MinWindowWidth = 600;

    /// <summary>
    /// 窗口最小高度（像素）。
    /// </summary>
    private const int MinWindowHeight = 400;

    /// <summary>
    /// 窗口过程消息：获取最小最大尺寸信息。
    /// </summary>
    private const uint WmGetMinMaxInfo = 0x0024;

    /// <summary>
    /// SetWindowLong 参数：窗口过程。
    /// </summary>
    private const int GwlpWndProc = -4;

    /// <summary>
    /// 窗口过程委托引用，防止被垃圾回收。
    /// </summary>
    private WndProcDelegate? _wndProcDelegate;

    /// <summary>
    /// 原始窗口过程指针。
    /// </summary>
    private IntPtr _oldWndProc = IntPtr.Zero;

    /// <summary>
    /// 窗口句柄。
    /// </summary>
    private IntPtr _hWnd = IntPtr.Zero;

    /// <summary>
    /// 标识当前是否处于设置页面。
    /// </summary>
    private bool _isInSettingsPage;

    /// <summary>
    /// 初始化主窗口实例。
    /// </summary>
    /// <remarks>
    /// 执行顺序：初始化组件 → 设置窗口尺寸限制 → 配置标题栏 → 导航到编辑器页面。
    /// </remarks>
    public MainWindow()
    {
        InitializeComponent();

        InitializeWindowMinSize();
        ConfigureTitleBar();
        UpdateTitleText();

        ContentFrame.Navigate(typeof(Editor));
        _isInSettingsPage = false;
        UpdateTitleBarButtons();

        Closed += OnWindowClosed;
    }

    /// <summary>
    /// 初始化窗口最小尺寸限制。
    /// </summary>
    /// <remarks>
    /// 通过子类化窗口过程拦截 WM_GETMINMAXINFO 消息实现。
    /// </remarks>
    private void InitializeWindowMinSize()
    {
        _hWnd = this.As<IWindowNative>().WindowHandle;
        _wndProcDelegate = new WndProcDelegate(WindowProc);
        _oldWndProc = SetWindowLong(_hWnd, GwlpWndProc, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
    }

    /// <summary>
    /// 窗口关闭事件处理程序，恢复原始窗口过程。
    /// </summary>
    /// <param name="sender">事件源，即当前窗口实例。</param>
    /// <param name="args">窗口关闭事件参数。</param>
    /// <remarks>
    /// 必须在窗口关闭前恢复原始窗口过程，否则可能导致访问冲突。
    /// </remarks>
    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (_hWnd != IntPtr.Zero && _oldWndProc != IntPtr.Zero)
        {
            SetWindowLong(_hWnd, GwlpWndProc, _oldWndProc);
            _oldWndProc = IntPtr.Zero;
        }

        _wndProcDelegate = null;
        _hWnd = IntPtr.Zero;
    }

    /// <summary>
    /// 自定义窗口过程，处理窗口最小尺寸限制。
    /// </summary>
    /// <param name="hWnd">窗口句柄。</param>
    /// <param name="uMsg">消息标识符。</param>
    /// <param name="wParam">消息参数。</param>
    /// <param name="lParam">消息参数。</param>
    /// <returns>消息处理结果。</returns>
    private IntPtr WindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam)
    {
        if (uMsg == WmGetMinMaxInfo)
        {
            uint dpi = GetDpiForWindow(hWnd);
            MINMAXINFO info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            info.ptMinTrackSize.x = (MinWindowWidth * (int)dpi + 48) / 96;
            info.ptMinTrackSize.y = (MinWindowHeight * (int)dpi + 48) / 96;
            Marshal.StructureToPtr(info, lParam, true);
        }

        return CallWindowProc(_oldWndProc, hWnd, uMsg, wParam, lParam);
    }

    /// <summary>
    /// 配置沉浸式标题栏。
    /// </summary>
    /// <remarks>
    /// 将窗口内容扩展至标题栏区域，并指定自定义标题栏元素。
    /// </remarks>
    private void ConfigureTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
    }

    /// <summary>
    /// 更新标题栏文本为本地化名称。
    /// </summary>
    private void UpdateTitleText()
    {
        AppTitleTextBlock.Text = Text.Localize("一次运行");
    }

    /// <summary>
    /// 导航到编辑器页面。
    /// </summary>
    /// <remarks>
    /// 使用从右向左的滑动动画。
    /// </remarks>
    private void NavigateToEditor()
    {
        ContentFrame.Navigate(
            typeof(Editor),
            null,
            new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromLeft });

        _isInSettingsPage = false;
        UpdateTitleBarButtons();
    }

    /// <summary>
    /// 导航到设置页面。
    /// </summary>
    /// <remarks>
    /// 使用从左向右的滑动动画。
    /// </remarks>
    private void NavigateToSettings()
    {
        ContentFrame.Navigate(
            typeof(Settings),
            null,
            new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });

        _isInSettingsPage = true;
        UpdateTitleBarButtons();
    }

    /// <summary>
    /// 更新标题栏按钮可见性。
    /// </summary>
    /// <remarks>
    /// 设置页面显示返回按钮，编辑器页面显示设置按钮。
    /// </remarks>
    private void UpdateTitleBarButtons()
    {
        BackButton.Visibility = _isInSettingsPage ? Visibility.Visible : Visibility.Collapsed;
        SettingsButton.Visibility = _isInSettingsPage ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// 设置按钮点击事件处理程序。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">路由事件参数。</param>
    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateToSettings();
    }

    /// <summary>
    /// 返回按钮点击事件处理程序。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">路由事件参数。</param>
    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateToEditor();
    }

    #region Win32 API

    /// <summary>
    /// 窗口过程委托类型。
    /// </summary>
    /// <param name="hWnd">窗口句柄。</param>
    /// <param name="msg">消息标识符。</param>
    /// <param name="wParam">消息参数。</param>
    /// <param name="lParam">消息参数。</param>
    /// <returns>消息处理结果。</returns>
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// 表示屏幕坐标点的结构体。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        /// <summary>X 坐标。</summary>
        public int x;

        /// <summary>Y 坐标。</summary>
        public int y;
    }

    /// <summary>
    /// 窗口最大最小尺寸信息结构体。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        /// <summary>保留字段。</summary>
        public POINT ptReserved;

        /// <summary>窗口最大化时的尺寸。</summary>
        public POINT ptMaxSize;

        /// <summary>窗口最大化时的位置。</summary>
        public POINT ptMaxPosition;

        /// <summary>窗口最小跟踪尺寸。</summary>
        public POINT ptMinTrackSize;

        /// <summary>窗口最大跟踪尺寸。</summary>
        public POINT ptMaxTrackSize;
    }

    /// <summary>
    /// 窗口原生接口，提供窗口句柄访问。
    /// </summary>
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("EECDBF0E-BAE9-4CB6-A68E-9598E1CB57BB")]
    private interface IWindowNative
    {
        /// <summary>
        /// 获取窗口句柄。
        /// </summary>
        IntPtr WindowHandle { get; }
    }

    /// <summary>
    /// 设置窗口长整型属性。
    /// </summary>
    /// <param name="hWnd">窗口句柄。</param>
    /// <param name="nIndex">属性索引。</param>
    /// <param name="dwNewLong">新属性值。</param>
    /// <returns>原属性值。</returns>
    private static IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
            : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
    }

    /// <summary>
    /// 32 位 SetWindowLong 函数。
    /// </summary>
    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    /// <summary>
    /// 64 位 SetWindowLongPtr 函数。
    /// </summary>
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    /// <summary>
    /// 调用原始窗口过程。
    /// </summary>
    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// 获取窗口 DPI 值。
    /// </summary>
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    #endregion
}