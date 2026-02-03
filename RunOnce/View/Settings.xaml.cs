/*
 * 设置页面
 * Demo 占位页面，实际功能将在后续版本实现
 * 
 * @author: WaterRun
 * @file: View/Settings.xaml.cs
 * @date: 2026-02-03
 */

#nullable enable

using Microsoft.UI.Xaml.Controls;
using RunOnce.Static;

namespace RunOnce.View;

/// <summary>
/// 设置页面，当前为 Demo 占位页面。
/// </summary>
/// <remarks>
/// 不变量：页面加载后标题文本已完成本地化初始化。
/// 线程安全：所有成员必须在 UI 线程访问。
/// 副作用：无。
/// </remarks>
public sealed partial class Settings : Page
{
    /// <summary>
    /// 初始化设置页面实例。
    /// </summary>
    public Settings()
    {
        InitializeComponent();
        PageTitleText.Text = Text.Localize("设置");
    }
}