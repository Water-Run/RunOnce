/*
 * 代码编辑器页面
 * Demo 占位页面，实际功能将在后续版本实现
 * 
 * @author: WaterRun
 * @file: View/Editor.xaml.cs
 * @date: 2026-02-03
 */

#nullable enable

using Microsoft.UI.Xaml.Controls;
using RunOnce.Static;

namespace RunOnce.View;

/// <summary>
/// 代码编辑器页面，当前为 Demo 占位页面。
/// </summary>
/// <remarks>
/// 不变量：页面加载后标题文本已完成本地化初始化。
/// 线程安全：所有成员必须在 UI 线程访问。
/// 副作用：无。
/// </remarks>
public sealed partial class Editor : Page
{
    /// <summary>
    /// 初始化编辑器页面实例。
    /// </summary>
    public Editor()
    {
        InitializeComponent();
        PageTitleText.Text = Text.Localize("编辑器");
    }
}