/*
 * 应用程序入口与生命周期管理
 * 负责应用程序初始化、主窗口创建及全局主题管理
 * 
 * @author: WaterRun
 * @file: App.xaml.cs
 * @date: 2026-02-03
 */

#nullable enable

using Microsoft.UI.Xaml;
using RunOnce.Static;

namespace RunOnce;

/// <summary>
/// 应用程序主类，管理应用生命周期与全局状态。
/// </summary>
/// <remarks>
/// 不变量：应用程序实例全局唯一；主窗口在 OnLaunched 后必定存在。
/// 线程安全：UI 操作必须在主线程执行。
/// 副作用：初始化时创建主窗口并应用主题设置。
/// </remarks>
public partial class App : Application
{
    /// <summary>
    /// 主窗口实例的内部引用。
    /// </summary>
    private Window? _mainWindow;

    /// <summary>
    /// 获取当前应用程序的主窗口实例。
    /// </summary>
    /// <value>应用程序激活后返回主窗口实例；激活前返回 null。</value>
    public Window? MainWindow => _mainWindow;

    /// <summary>
    /// 初始化应用程序实例。
    /// </summary>
    public App()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 应用程序启动时的入口点。
    /// </summary>
    /// <param name="args">启动参数，包含激活类型与激活数据。</param>
    /// <remarks>
    /// 执行顺序：创建主窗口 → 应用主题 → 激活窗口。
    /// </remarks>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _mainWindow = new MainWindow();
        ApplyTheme(Config.Theme);
        _mainWindow.Activate();
    }

    /// <summary>
    /// 应用指定主题到应用程序根元素。
    /// </summary>
    /// <param name="theme">目标主题风格枚举值。</param>
    /// <remarks>
    /// 仅影响当前窗口内容的 RequestedTheme 属性，不修改系统设置。
    /// </remarks>
    public void ApplyTheme(ThemeStyle theme)
    {
        if (_mainWindow?.Content is FrameworkElement rootElement)
        {
            rootElement.RequestedTheme = theme switch
            {
                ThemeStyle.Light => ElementTheme.Light,
                ThemeStyle.Dark => ElementTheme.Dark,
                ThemeStyle.FollowSystem => ElementTheme.Default,
                _ => ElementTheme.Default
            };
        }
    }

    /// <summary>
    /// 更新主题设置并持久化到配置。
    /// </summary>
    /// <param name="theme">目标主题风格枚举值。</param>
    /// <remarks>
    /// 同时更新 Config.Theme 与当前窗口主题。
    /// </remarks>
    public void UpdateTheme(ThemeStyle theme)
    {
        Config.Theme = theme;
        ApplyTheme(theme);
    }
}