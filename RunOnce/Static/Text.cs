/*
 * 国际化文本管理
 * 提供应用程序界面文本的多语言支持，以中文为基础语言，支持英文翻译
 * 
 * @author: WaterRun
 * @file: Static/Text.cs
 * @date: 2026-01-28
 */

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace RunOnce.Static;

/// <summary>
/// 国际化文本管理静态类，提供应用程序界面文本的多语言支持。
/// </summary>
/// <remarks>
/// 不变量：所有文本键均以中文原文作为标识符；翻译字典在类型初始化时构建且不可变。
/// 线程安全：所有公开方法均为线程安全，内部字典为只读。
/// 副作用：无。
/// </remarks>
public static class Text
{
    /// <summary>
    /// 中文到英文的翻译映射字典，键为中文原文，值为英文译文。
    /// </summary>
    private static readonly Dictionary<string, string> _chineseToEnglish = new()
    {
        // 应用程序基础信息
        ["一次运行"] = "RunOnce",
        ["版本"] = "Version",
        ["作者"] = "Author",

        // 主题风格
        ["跟随系统"] = "Follow System",
        ["浅色"] = "Light",
        ["深色"] = "Dark",
        ["主题风格"] = "Theme Style",

        // 显示语言
        ["显示语言"] = "Display Language",
        ["简体中文"] = "Simplified Chinese",
        ["英文"] = "English",

        // 语言选择器模式
        ["始终显示"] = "Always Show",
        ["高可信时隐藏"] = "Hide When High Confidence",
        ["仅无可信时显示"] = "Show Only When No Confidence",
        ["执行前语言选择框"] = "Language Selector Before Execution",

        // 执行相关
        ["执行"] = "Execute",
        ["执行前确认"] = "Confirm Before Execution",
        ["确认执行"] = "Confirm Execution",
        ["取消"] = "Cancel",
        ["确定"] = "OK",
        ["运行"] = "Run",
        ["停止"] = "Stop",

        // 语言识别
        ["最可能"] = "Most Likely",
        ["次可能"] = "Less Likely",
        ["手动指定"] = "Manual Selection",
        ["语言识别"] = "Language Detection",
        ["置信度"] = "Confidence",
        ["高置信度"] = "High Confidence",
        ["中置信度"] = "Medium Confidence",
        ["低置信度"] = "Low Confidence",

        // 设置
        ["设置"] = "Settings",
        ["常规设置"] = "General Settings",
        ["语言指令配置"] = "Language Command Configuration",
        ["临时文件名前缀"] = "Temp File Prefix",
        ["置信度范围"] = "Confidence Range",
        ["下界"] = "Lower Bound",
        ["上界"] = "Upper Bound",
        ["重置"] = "Reset",
        ["重置全部"] = "Reset All",
        ["恢复默认"] = "Restore Defaults",
        ["保存"] = "Save",
        ["应用"] = "Apply",

        // 编辑器
        ["编辑器"] = "Editor",
        ["新建"] = "New",
        ["打开"] = "Open",
        ["保存文件"] = "Save File",
        ["复制"] = "Copy",
        ["粘贴"] = "Paste",
        ["剪切"] = "Cut",
        ["撤销"] = "Undo",
        ["重做"] = "Redo",
        ["全选"] = "Select All",
        ["清空"] = "Clear",
        ["在此运行代码"] = "Run Code Here",

        // 脚本语言名称
        ["批处理"] = "Batch",

        // 文件操作
        ["临时文件"] = "Temporary File",
        ["文件已创建"] = "File Created",
        ["文件已删除"] = "File Deleted",
        ["执行完成"] = "Execution Completed",
        ["执行失败"] = "Execution Failed",

        // 错误与警告
        ["错误"] = "Error",
        ["警告"] = "Warning",
        ["信息"] = "Information",
        ["未知错误"] = "Unknown Error",
        ["操作失败"] = "Operation Failed",
        ["参数无效"] = "Invalid Parameter",
        ["文件不存在"] = "File Not Found",
        ["权限不足"] = "Insufficient Permissions",
        ["语言不支持"] = "Language Not Supported",
        ["指令未配置"] = "Command Not Configured",

        // 确认对话框
        ["确认"] = "Confirm",
        ["是否确认执行此脚本？"] = "Are you sure you want to execute this script?",
        ["此操作不可撤销"] = "This action cannot be undone",
        ["是否重置所有设置？"] = "Reset all settings?",

        // 状态信息
        ["就绪"] = "Ready",
        ["正在执行..."] = "Executing...",
        ["正在识别语言..."] = "Detecting language...",
        ["已复制到剪贴板"] = "Copied to clipboard",

        // 关于
        ["关于"] = "About",
        ["开源许可"] = "Open Source License",
        ["GitHub 仓库"] = "GitHub Repository",
        ["微软商店"] = "Microsoft Store",
        ["检查更新"] = "Check for Updates",
        ["反馈问题"] = "Report Issue",

        // 右键菜单
        ["在此处运行脚本"] = "Run Script Here",
        ["使用一次运行打开"] = "Open with RunOnce",

        // 工具提示
        ["选择要执行的脚本语言"] = "Select the script language to execute",
        ["输入或粘贴代码"] = "Enter or paste code",
        ["点击执行脚本"] = "Click to execute script",
    };

    /// <summary>
    /// 获取指定中文文本的本地化翻译。
    /// </summary>
    /// <param name="chinese">中文原文，作为文本的唯一标识符，不允许为 null。</param>
    /// <returns>
    /// 根据当前应用程序语言设置返回对应的翻译文本；
    /// 若当前语言为中文或找不到对应翻译，则返回原始中文文本。
    /// </returns>
    /// <exception cref="ArgumentNullException">当 chinese 为 null 时抛出。</exception>
    /// <remarks>
    /// 语言解析优先级：用户显式设置 > 系统语言检测。
    /// 当设置为 FollowSystem 时，通过 CultureInfo.CurrentUICulture 判断是否为中文环境。
    /// </remarks>
    public static string Localize(string chinese)
    {
        ArgumentNullException.ThrowIfNull(chinese);

        if (ShouldUseChinese())
        {
            return chinese;
        }

        return _chineseToEnglish.TryGetValue(chinese, out string? english) ? english : chinese;
    }

    /// <summary>
    /// 获取指定中文文本的本地化翻译，支持格式化参数。
    /// </summary>
    /// <param name="chinese">中文原文模板，支持 string.Format 占位符，不允许为 null。</param>
    /// <param name="args">格式化参数数组，不允许为 null。</param>
    /// <returns>
    /// 根据当前语言设置返回格式化后的翻译文本；
    /// 若当前语言为中文或找不到对应翻译，则使用原始中文模板进行格式化。
    /// </returns>
    /// <exception cref="ArgumentNullException">当 chinese 或 args 为 null 时抛出。</exception>
    /// <exception cref="FormatException">当格式化字符串与参数不匹配时抛出。</exception>
    public static string Localize(string chinese, params object[] args)
    {
        ArgumentNullException.ThrowIfNull(chinese);
        ArgumentNullException.ThrowIfNull(args);

        string template = Localize(chinese);
        return string.Format(CultureInfo.CurrentCulture, template, args);
    }

    /// <summary>
    /// 判断当前是否应使用中文显示。
    /// </summary>
    /// <returns>若应使用中文则返回 true，否则返回 false。</returns>
    private static bool ShouldUseChinese()
    {
        DisplayLanguage language = Config.Language;

        return language switch
        {
            DisplayLanguage.Chinese => true,
            DisplayLanguage.English => false,
            DisplayLanguage.FollowSystem => IsSystemChinese(),
            _ => IsSystemChinese()
        };
    }

    /// <summary>
    /// 判断系统当前 UI 文化是否为中文。
    /// </summary>
    /// <returns>若系统 UI 文化为中文（zh 开头）则返回 true，否则返回 false。</returns>
    private static bool IsSystemChinese()
    {
        string cultureName = CultureInfo.CurrentUICulture.Name;
        return cultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 获取当前实际使用的显示语言。
    /// </summary>
    /// <returns>
    /// 返回实际生效的语言：当设置为 FollowSystem 时返回系统检测结果，否则返回用户设置值。
    /// </returns>
    /// <remarks>
    /// 此方法用于需要明确知道当前使用何种语言的场景，例如日志记录或调试。
    /// </remarks>
    public static DisplayLanguage GetEffectiveLanguage()
    {
        DisplayLanguage language = Config.Language;

        if (language == DisplayLanguage.FollowSystem)
        {
            return IsSystemChinese() ? DisplayLanguage.Chinese : DisplayLanguage.English;
        }

        return language;
    }

    /// <summary>
    /// 检查指定的中文文本是否存在英文翻译。
    /// </summary>
    /// <param name="chinese">待检查的中文原文，不允许为 null。</param>
    /// <returns>若存在英文翻译则返回 true，否则返回 false。</returns>
    /// <exception cref="ArgumentNullException">当 chinese 为 null 时抛出。</exception>
    /// <remarks>
    /// 此方法用于开发期间检查翻译覆盖率，生产环境中不建议频繁调用。
    /// </remarks>
    public static bool HasTranslation(string chinese)
    {
        ArgumentNullException.ThrowIfNull(chinese);
        return _chineseToEnglish.ContainsKey(chinese);
    }

    /// <summary>
    /// 获取所有已注册的翻译条目数量。
    /// </summary>
    /// <value>翻译字典中的条目总数，用于开发期间的覆盖率统计。</value>
    public static int TranslationCount => _chineseToEnglish.Count;
}