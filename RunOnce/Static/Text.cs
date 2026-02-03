/*
 * 国际化文本管理
 * 提供应用程序界面文本的多语言支持，以中文为基础语言，支持英文翻译
 * 
 * @author: WaterRun
 * @file: Static/Text.cs
 * @date: 2026-02-03
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

        // 主题风格（Config.cs 使用）
        ["跟随系统"] = "Follow System",
        ["浅色"] = "Light",
        ["深色"] = "Dark",

        // 显示语言（Config.cs 使用）
        ["简体中文"] = "Simplified Chinese",
        ["英文"] = "English",

        // 页面标题
        ["编辑器"] = "Editor",
        ["设置"] = "Settings",
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
    /// 语言解析优先级：用户显式设置 &gt; 系统语言检测。
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