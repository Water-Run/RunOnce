/*
 * 应用程序配置管理
 * 提供用户设置项与硬编码常量的统一访问入口，支持持久化存储
 * 
 * @author: WaterRun
 * @file: Static/Config.cs
 * @date: 2026-01-28
 */

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Windows.Storage;

namespace RunOnce.Static;

/// <summary>
/// 主题风格枚举，定义应用程序的视觉主题模式。
/// </summary>
public enum ThemeStyle
{
    /// <summary>跟随系统主题设置。</summary>
    FollowSystem,

    /// <summary>强制使用浅色主题。</summary>
    Light,

    /// <summary>强制使用深色主题。</summary>
    Dark,
}

/// <summary>
/// 显示语言枚举，定义应用程序的界面语言。
/// </summary>
public enum DisplayLanguage
{
    /// <summary>跟随系统语言设置。</summary>
    FollowSystem,

    /// <summary>简体中文。</summary>
    Chinese,

    /// <summary>英文。</summary>
    English,
}

/// <summary>
/// 语言选择框显示模式枚举，定义执行前语言选择框的显示策略。
/// </summary>
public enum LanguageSelectorMode
{
    /// <summary>始终显示语言选择框。</summary>
    AlwaysShow,

    /// <summary>当存在高可信度语言识别结果时不显示（默认行为）。</summary>
    HideWhenHighConfidence,

    /// <summary>仅当无法识别任何可信语言时显示。</summary>
    ShowOnlyWhenNoConfidence,
}

/// <summary>
/// 置信度范围结构体，定义语言识别置信度的判定区间。
/// </summary>
/// <remarks>
/// 不变量：LowerBound 必须小于等于 UpperBound；两者均在 [0.0, 1.0] 范围内。
/// 线程安全：作为不可变值类型，天然线程安全。
/// 副作用：无。
/// </remarks>
public readonly record struct ConfidenceRange
{
    /// <summary>
    /// 置信度下界的默认值。
    /// </summary>
    public const double DefaultLowerBound = 0.7;

    /// <summary>
    /// 置信度上界的默认值。
    /// </summary>
    public const double DefaultUpperBound = 0.95;

    /// <summary>
    /// 置信度的最小允许值。
    /// </summary>
    public const double MinValue = 0.0;

    /// <summary>
    /// 置信度的最大允许值。
    /// </summary>
    public const double MaxValue = 1.0;

    /// <summary>
    /// 获取置信度判定的下界阈值。
    /// </summary>
    /// <value>
    /// 范围 [0.0, 1.0]，低于此值判定为无置信度。
    /// </value>
    public double LowerBound { get; }

    /// <summary>
    /// 获取置信度判定的上界阈值。
    /// </summary>
    /// <value>
    /// 范围 [0.0, 1.0]，高于此值判定为高置信度。
    /// </value>
    public double UpperBound { get; }

    /// <summary>
    /// 使用指定的上下界创建置信度范围实例。
    /// </summary>
    /// <param name="lowerBound">下界阈值，范围 [0.0, 1.0]，必须小于等于 upperBound。</param>
    /// <param name="upperBound">上界阈值，范围 [0.0, 1.0]，必须大于等于 lowerBound。</param>
    /// <exception cref="ArgumentOutOfRangeException">当 lowerBound 或 upperBound 超出 [0.0, 1.0] 范围时抛出。</exception>
    /// <exception cref="ArgumentException">当 lowerBound 大于 upperBound 时抛出。</exception>
    public ConfidenceRange(double lowerBound, double upperBound)
    {
        if (lowerBound < MinValue || lowerBound > MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(lowerBound), lowerBound, Text.Localize("下界必须在 [0.0, 1.0] 范围内。"));
        }
        if (upperBound < MinValue || upperBound > MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(upperBound), upperBound, Text.Localize("上界必须在 [0.0, 1.0] 范围内。"));
        }
        if (lowerBound > upperBound)
        {
            throw new ArgumentException(Text.Localize("下界不能大于上界。"), nameof(lowerBound));
        }

        LowerBound = lowerBound;
        UpperBound = upperBound;
    }

    /// <summary>
    /// 获取使用默认阈值的置信度范围实例。
    /// </summary>
    /// <value>
    /// 下界为 0.7，上界为 0.95 的默认置信度范围。
    /// </value>
    public static ConfidenceRange Default => new(DefaultLowerBound, DefaultUpperBound);

    /// <summary>
    /// 判断给定的置信度值是否属于低置信度（无置信）。
    /// </summary>
    /// <param name="confidence">待判定的置信度值，范围 [0.0, 1.0]。</param>
    /// <returns>若置信度低于下界则返回 true，否则返回 false。</returns>
    public bool IsLowConfidence(double confidence) => confidence < LowerBound;

    /// <summary>
    /// 判断给定的置信度值是否属于高置信度。
    /// </summary>
    /// <param name="confidence">待判定的置信度值，范围 [0.0, 1.0]。</param>
    /// <returns>若置信度高于上界则返回 true，否则返回 false。</returns>
    public bool IsHighConfidence(double confidence) => confidence > UpperBound;

    /// <summary>
    /// 判断给定的置信度值是否属于中间范围（既非低也非高）。
    /// </summary>
    /// <param name="confidence">待判定的置信度值，范围 [0.0, 1.0]。</param>
    /// <returns>若置信度在 [LowerBound, UpperBound] 范围内则返回 true，否则返回 false。</returns>
    public bool IsMiddleConfidence(double confidence) => confidence >= LowerBound && confidence <= UpperBound;

    /// <summary>
    /// 对给定的置信度值进行分类。
    /// </summary>
    /// <param name="confidence">待分类的置信度值，范围 [0.0, 1.0]。</param>
    /// <returns>返回 -1 表示低置信度，0 表示中间范围，1 表示高置信度。</returns>
    public int Classify(double confidence) => confidence < LowerBound ? -1 : (confidence > UpperBound ? 1 : 0);
}

/// <summary>
/// 应用程序配置静态类，提供所有用户设置项的读写与持久化，以及硬编码常量的访问。
/// </summary>
/// <remarks>
/// 不变量：所有设置项在首次访问时自动从本地存储加载或使用默认值初始化。
/// 线程安全：通过锁机制保证并发访问安全。
/// 副作用：Setter 操作会立即触发本地持久化存储写入。
/// </remarks>
public static class Config
{
    /// <summary>
    /// 用于线程同步的锁对象。
    /// </summary>
    private static readonly object _syncLock = new();

    /// <summary>
    /// 本地设置存储容器的引用缓存。
    /// </summary>
    private static readonly ApplicationDataContainer _localSettings = ApplicationData.Current.LocalSettings;

    /// <summary>
    /// 语言执行指令配置的内存缓存，延迟初始化。
    /// </summary>
    private static Dictionary<string, string>? _languageCommands;

    /// <summary>
    /// 标识语言指令配置是否已从存储加载。
    /// </summary>
    private static bool _languageCommandsLoaded;

    #region 硬编码常量

    /// <summary>软件的显示名称（中文）。</summary>
    /// <value>固定值 "一次运行"，不可更改。</value>
    public const string AppNameChinese = "一次运行";

    /// <summary>软件的显示名称（英文）。</summary>
    /// <value>固定值 "RunOnce"，不可更改。</value>
    public const string AppNameEnglish = "RunOnce";

    /// <summary>
    /// 获取本地化的软件显示名称。
    /// </summary>
    /// <value>根据当前语言设置返回中文或英文名称。</value>
    public static string AppName => Text.Localize(AppNameChinese);

    /// <summary>软件的当前版本号。</summary>
    /// <value>遵循语义化版本规范，格式为 Major.Minor.Patch。</value>
    public const string Version = "0.1.0";

    /// <summary>软件作者名称。</summary>
    /// <value>固定值 "WaterRun"。</value>
    public const string Author = "WaterRun";

    /// <summary>项目 GitHub 仓库地址。</summary>
    /// <value>指向作者的 GitHub 主页。</value>
    public const string GitHubUrl = "https://github.com/Water-Run";

    /// <summary>微软商店应用链接。</summary>
    /// <value>暂未发布，当前为空字符串。</value>
    public const string MicrosoftStoreUrl = "";

    /// <summary>支持的脚本语言列表。</summary>
    /// <value>包含所有可配置执行指令的语言标识符，只读数组。</value>
    public static IReadOnlyList<string> SupportedLanguages { get; } =
    [
        "bat",
        "powershell",
        "pwsh",
        "python",
        "lua",
        "nim",
        "php",
        "javascript",
        "typescript",
        "go",
        "vbscript"
    ];

    #endregion

    #region 设置项键名常量

    /// <summary>主题风格设置项的存储键名。</summary>
    private const string KeyThemeStyle = "ThemeStyle";

    /// <summary>显示语言设置项的存储键名。</summary>
    private const string KeyDisplayLanguage = "DisplayLanguage";

    /// <summary>语言执行指令配置的存储键名。</summary>
    private const string KeyLanguageCommands = "LanguageCommands";

    /// <summary>临时文件名前缀设置项的存储键名。</summary>
    private const string KeyTempFilePrefix = "TempFilePrefix";

    /// <summary>语言选择框显示模式设置项的存储键名。</summary>
    private const string KeyLanguageSelectorMode = "LanguageSelectorMode";

    /// <summary>执行前确认开关设置项的存储键名。</summary>
    private const string KeyConfirmBeforeExecution = "ConfirmBeforeExecution";

    /// <summary>置信度范围下界设置项的存储键名。</summary>
    private const string KeyConfidenceLowerBound = "ConfidenceLowerBound";

    /// <summary>置信度范围上界设置项的存储键名。</summary>
    private const string KeyConfidenceUpperBound = "ConfidenceUpperBound";

    #endregion

    #region 默认值常量

    /// <summary>临时文件名前缀的默认值。</summary>
    private const string DefaultTempFilePrefix = "__RunOnceTMP__";

    #endregion

    #region 设置项属性

    /// <summary>
    /// 获取或设置应用程序的主题风格。
    /// </summary>
    /// <value>
    /// ThemeStyle 枚举值，默认为 FollowSystem（跟随系统）。
    /// 设置时立即持久化到本地存储。
    /// </value>
    public static ThemeStyle Theme
    {
        get
        {
            lock (_syncLock)
            {
                return _localSettings.Values.TryGetValue(KeyThemeStyle, out object? value) && value is int intValue
                    ? (ThemeStyle)intValue
                    : ThemeStyle.FollowSystem;
            }
        }
        set
        {
            lock (_syncLock)
            {
                _localSettings.Values[KeyThemeStyle] = (int)value;
            }
        }
    }

    /// <summary>
    /// 获取或设置应用程序的显示语言。
    /// </summary>
    /// <value>
    /// DisplayLanguage 枚举值，默认为 FollowSystem（跟随系统）。
    /// 设置时立即持久化到本地存储。
    /// </value>
    public static DisplayLanguage Language
    {
        get
        {
            lock (_syncLock)
            {
                return _localSettings.Values.TryGetValue(KeyDisplayLanguage, out object? value) && value is int intValue
                    ? (DisplayLanguage)intValue
                    : DisplayLanguage.FollowSystem;
            }
        }
        set
        {
            lock (_syncLock)
            {
                _localSettings.Values[KeyDisplayLanguage] = (int)value;
            }
        }
    }

    /// <summary>
    /// 获取或设置临时文件名前缀。
    /// </summary>
    /// <value>
    /// 非空字符串，默认为 "__RunOnceTMP__"。
    /// 用于生成临时脚本文件时的文件名前缀标识。
    /// </value>
    /// <exception cref="ArgumentNullException">当设置值为 null 时抛出。</exception>
    /// <exception cref="ArgumentException">当设置值为空白字符串时抛出。</exception>
    public static string TempFilePrefix
    {
        get
        {
            lock (_syncLock)
            {
                return _localSettings.Values.TryGetValue(KeyTempFilePrefix, out object? value)
                       && value is string stringValue
                       && !string.IsNullOrWhiteSpace(stringValue)
                    ? stringValue
                    : DefaultTempFilePrefix;
            }
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(Text.Localize("临时文件名前缀不能为空白字符串。"), nameof(value));
            }
            lock (_syncLock)
            {
                _localSettings.Values[KeyTempFilePrefix] = value;
            }
        }
    }

    /// <summary>
    /// 获取或设置执行前语言选择框的显示模式。
    /// </summary>
    /// <value>
    /// LanguageSelectorMode 枚举值，默认为 HideWhenHighConfidence（有语言高可信时不显示）。
    /// 设置时立即持久化到本地存储。
    /// </value>
    public static LanguageSelectorMode SelectorMode
    {
        get
        {
            lock (_syncLock)
            {
                return _localSettings.Values.TryGetValue(KeyLanguageSelectorMode, out object? value) && value is int intValue
                    ? (LanguageSelectorMode)intValue
                    : LanguageSelectorMode.HideWhenHighConfidence;
            }
        }
        set
        {
            lock (_syncLock)
            {
                _localSettings.Values[KeyLanguageSelectorMode] = (int)value;
            }
        }
    }

    /// <summary>
    /// 获取或设置执行前是否需要用户确认。
    /// </summary>
    /// <value>
    /// 布尔值，true 表示执行前需要确认，false 表示直接执行。默认为 false（关闭）。
    /// 设置时立即持久化到本地存储。
    /// </value>
    public static bool ConfirmBeforeExecution
    {
        get
        {
            lock (_syncLock)
            {
                return _localSettings.Values.TryGetValue(KeyConfirmBeforeExecution, out object? value) && value is bool boolValue && boolValue;
            }
        }
        set
        {
            lock (_syncLock)
            {
                _localSettings.Values[KeyConfirmBeforeExecution] = value;
            }
        }
    }

    /// <summary>
    /// 获取或设置语言识别的置信度判定范围。
    /// </summary>
    /// <value>
    /// ConfidenceRange 结构体，定义低、中、高置信度的分界阈值。
    /// 默认下界为 0.7，上界为 0.95。
    /// 设置时立即持久化到本地存储。
    /// </value>
    /// <exception cref="ArgumentOutOfRangeException">当设置的范围边界超出 [0.0, 1.0] 时抛出。</exception>
    /// <exception cref="ArgumentException">当设置的下界大于上界时抛出。</exception>
    public static ConfidenceRange ConfidenceThreshold
    {
        get
        {
            lock (_syncLock)
            {
                double lowerBound = _localSettings.Values.TryGetValue(KeyConfidenceLowerBound, out object? lowerValue) && lowerValue is double lower
                    ? lower
                    : ConfidenceRange.DefaultLowerBound;

                double upperBound = _localSettings.Values.TryGetValue(KeyConfidenceUpperBound, out object? upperValue) && upperValue is double upper
                    ? upper
                    : ConfidenceRange.DefaultUpperBound;

                bool isValidRange = lowerBound >= ConfidenceRange.MinValue
                                    && lowerBound <= ConfidenceRange.MaxValue
                                    && upperBound >= ConfidenceRange.MinValue
                                    && upperBound <= ConfidenceRange.MaxValue
                                    && lowerBound <= upperBound;

                return isValidRange ? new ConfidenceRange(lowerBound, upperBound) : ConfidenceRange.Default;
            }
        }
        set
        {
            lock (_syncLock)
            {
                _localSettings.Values[KeyConfidenceLowerBound] = value.LowerBound;
                _localSettings.Values[KeyConfidenceUpperBound] = value.UpperBound;
            }
        }
    }

    #endregion

    #region 语言指令配置

    /// <summary>
    /// 获取指定脚本语言的执行指令。
    /// </summary>
    /// <param name="language">
    /// 脚本语言标识符，必须是 SupportedLanguages 中定义的有效值，不区分大小写。
    /// 不允许为 null 或空白字符串。
    /// </param>
    /// <returns>
    /// 该语言对应的执行指令字符串；若未配置则返回该语言的默认指令。
    /// </returns>
    /// <exception cref="ArgumentNullException">当 language 为 null 时抛出。</exception>
    /// <exception cref="ArgumentException">当 language 为空白字符串或不在支持列表中时抛出。</exception>
    public static string GetLanguageCommand(string language)
    {
        ArgumentNullException.ThrowIfNull(language);
        if (string.IsNullOrWhiteSpace(language))
        {
            throw new ArgumentException(Text.Localize("语言标识符不能为空白字符串。"), nameof(language));
        }

        string normalizedLanguage = language.ToLowerInvariant();
        if (!SupportedLanguages.Contains(normalizedLanguage))
        {
            throw new ArgumentException(Text.Localize("不支持的语言标识符: {0}。", language), nameof(language));
        }

        lock (_syncLock)
        {
            EnsureLanguageCommandsLoaded();
            return _languageCommands!.TryGetValue(normalizedLanguage, out string? command)
                ? command
                : GetDefaultLanguageCommand(normalizedLanguage);
        }
    }

    /// <summary>
    /// 设置指定脚本语言的执行指令。
    /// </summary>
    /// <param name="language">
    /// 脚本语言标识符，必须是 SupportedLanguages 中定义的有效值，不区分大小写。
    /// 不允许为 null 或空白字符串。
    /// </param>
    /// <param name="command">
    /// 执行指令字符串，不允许为 null 或空白字符串。
    /// </param>
    /// <exception cref="ArgumentNullException">当 language 或 command 为 null 时抛出。</exception>
    /// <exception cref="ArgumentException">当参数为空白字符串或 language 不在支持列表中时抛出。</exception>
    /// <remarks>
    /// 设置后立即持久化到本地存储。
    /// </remarks>
    public static void SetLanguageCommand(string language, string command)
    {
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(language))
        {
            throw new ArgumentException(Text.Localize("语言标识符不能为空白字符串。"), nameof(language));
        }
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException(Text.Localize("执行指令不能为空白字符串。"), nameof(command));
        }

        string normalizedLanguage = language.ToLowerInvariant();
        if (!SupportedLanguages.Contains(normalizedLanguage))
        {
            throw new ArgumentException(Text.Localize("不支持的语言标识符: {0}。", language), nameof(language));
        }

        lock (_syncLock)
        {
            EnsureLanguageCommandsLoaded();
            _languageCommands![normalizedLanguage] = command;
            PersistLanguageCommands();
        }
    }

    /// <summary>
    /// 获取所有语言的执行指令配置副本。
    /// </summary>
    /// <returns>
    /// 包含所有已配置语言及其执行指令的字典副本；未配置的语言将使用默认值填充。
    /// </returns>
    /// <remarks>
    /// 返回的是配置数据的深拷贝，修改返回值不会影响内部存储。
    /// </remarks>
    public static Dictionary<string, string> GetAllLanguageCommands()
    {
        lock (_syncLock)
        {
            EnsureLanguageCommandsLoaded();
            return SupportedLanguages.ToDictionary(
                lang => lang,
                lang => _languageCommands!.TryGetValue(lang, out string? cmd) ? cmd : GetDefaultLanguageCommand(lang),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 将指定语言的执行指令重置为默认值。
    /// </summary>
    /// <param name="language">
    /// 脚本语言标识符，必须是 SupportedLanguages 中定义的有效值，不区分大小写。
    /// 不允许为 null 或空白字符串。
    /// </param>
    /// <exception cref="ArgumentNullException">当 language 为 null 时抛出。</exception>
    /// <exception cref="ArgumentException">当 language 为空白字符串或不在支持列表中时抛出。</exception>
    /// <remarks>
    /// 重置后立即持久化到本地存储。
    /// </remarks>
    public static void ResetLanguageCommand(string language)
    {
        ArgumentNullException.ThrowIfNull(language);
        if (string.IsNullOrWhiteSpace(language))
        {
            throw new ArgumentException(Text.Localize("语言标识符不能为空白字符串。"), nameof(language));
        }

        string normalizedLanguage = language.ToLowerInvariant();
        if (!SupportedLanguages.Contains(normalizedLanguage))
        {
            throw new ArgumentException(Text.Localize("不支持的语言标识符: {0}。", language), nameof(language));
        }

        lock (_syncLock)
        {
            EnsureLanguageCommandsLoaded();
            _languageCommands![normalizedLanguage] = GetDefaultLanguageCommand(normalizedLanguage);
            PersistLanguageCommands();
        }
    }

    /// <summary>
    /// 将所有语言的执行指令重置为默认值。
    /// </summary>
    /// <remarks>
    /// 重置后立即持久化到本地存储。
    /// </remarks>
    public static void ResetAllLanguageCommands()
    {
        lock (_syncLock)
        {
            _languageCommands = CreateDefaultLanguageCommands();
            _languageCommandsLoaded = true;
            PersistLanguageCommands();
        }
    }

    #endregion

    #region 本地化辅助方法

    /// <summary>
    /// 获取主题风格枚举值的本地化显示名称。
    /// </summary>
    /// <param name="theme">主题风格枚举值。</param>
    /// <returns>本地化后的显示名称字符串。</returns>
    public static string GetThemeDisplayName(ThemeStyle theme)
    {
        return theme switch
        {
            ThemeStyle.FollowSystem => Text.Localize("跟随系统"),
            ThemeStyle.Light => Text.Localize("浅色"),
            ThemeStyle.Dark => Text.Localize("深色"),
            _ => theme.ToString()
        };
    }

    /// <summary>
    /// 获取显示语言枚举值的本地化显示名称。
    /// </summary>
    /// <param name="language">显示语言枚举值。</param>
    /// <returns>本地化后的显示名称字符串。</returns>
    public static string GetLanguageDisplayName(DisplayLanguage language)
    {
        return language switch
        {
            DisplayLanguage.FollowSystem => Text.Localize("跟随系统"),
            DisplayLanguage.Chinese => Text.Localize("简体中文"),
            DisplayLanguage.English => Text.Localize("英文"),
            _ => language.ToString()
        };
    }

    /// <summary>
    /// 获取语言选择器模式枚举值的本地化显示名称。
    /// </summary>
    /// <param name="mode">语言选择器模式枚举值。</param>
    /// <returns>本地化后的显示名称字符串。</returns>
    public static string GetSelectorModeDisplayName(LanguageSelectorMode mode)
    {
        return mode switch
        {
            LanguageSelectorMode.AlwaysShow => Text.Localize("始终显示"),
            LanguageSelectorMode.HideWhenHighConfidence => Text.Localize("高可信时隐藏"),
            LanguageSelectorMode.ShowOnlyWhenNoConfidence => Text.Localize("仅无可信时显示"),
            _ => mode.ToString()
        };
    }

    #endregion

    #region 重置方法

    /// <summary>
    /// 将所有用户设置项重置为默认值。
    /// </summary>
    /// <remarks>
    /// 包括主题风格、显示语言、临时文件前缀、语言选择框模式、执行确认开关、置信度范围以及所有语言执行指令。
    /// 重置后立即持久化到本地存储。
    /// </remarks>
    public static void ResetAllSettings()
    {
        lock (_syncLock)
        {
            _localSettings.Values[KeyThemeStyle] = (int)ThemeStyle.FollowSystem;
            _localSettings.Values[KeyDisplayLanguage] = (int)DisplayLanguage.FollowSystem;
            _localSettings.Values[KeyTempFilePrefix] = DefaultTempFilePrefix;
            _localSettings.Values[KeyLanguageSelectorMode] = (int)LanguageSelectorMode.HideWhenHighConfidence;
            _localSettings.Values[KeyConfirmBeforeExecution] = false;
            _localSettings.Values[KeyConfidenceLowerBound] = ConfidenceRange.DefaultLowerBound;
            _localSettings.Values[KeyConfidenceUpperBound] = ConfidenceRange.DefaultUpperBound;
            _languageCommands = CreateDefaultLanguageCommands();
            _languageCommandsLoaded = true;
            PersistLanguageCommands();
        }
    }

    /// <summary>
    /// 将置信度范围重置为默认值。
    /// </summary>
    /// <remarks>
    /// 重置后立即持久化到本地存储。
    /// </remarks>
    public static void ResetConfidenceThreshold()
    {
        lock (_syncLock)
        {
            _localSettings.Values[KeyConfidenceLowerBound] = ConfidenceRange.DefaultLowerBound;
            _localSettings.Values[KeyConfidenceUpperBound] = ConfidenceRange.DefaultUpperBound;
        }
    }

    #endregion

    #region 私有辅助方法

    /// <summary>
    /// 确保语言指令配置已从存储加载到内存缓存。
    /// </summary>
    /// <remarks>
    /// 必须在持有 _syncLock 的情况下调用。
    /// 若存储中无数据或数据损坏，将使用默认值初始化。
    /// </remarks>
    private static void EnsureLanguageCommandsLoaded()
    {
        if (_languageCommandsLoaded)
        {
            return;
        }

        if (_localSettings.Values.TryGetValue(KeyLanguageCommands, out object? value)
            && value is string jsonString
            && !string.IsNullOrWhiteSpace(jsonString))
        {
            try
            {
                Dictionary<string, string>? deserialized = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonString);
                if (deserialized is not null)
                {
                    _languageCommands = new Dictionary<string, string>(deserialized, StringComparer.OrdinalIgnoreCase);
                    _languageCommandsLoaded = true;
                    return;
                }
            }
            catch (JsonException)
            {
                // 数据损坏时静默回退到默认值，符合配置类的容错设计
            }
        }

        _languageCommands = CreateDefaultLanguageCommands();
        _languageCommandsLoaded = true;
    }

    /// <summary>
    /// 将当前内存中的语言指令配置持久化到本地存储。
    /// </summary>
    /// <remarks>
    /// 必须在持有 _syncLock 的情况下调用。
    /// 使用 JSON 格式序列化存储。
    /// </remarks>
    private static void PersistLanguageCommands()
    {
        if (_languageCommands is null)
        {
            return;
        }

        string jsonString = JsonSerializer.Serialize(_languageCommands);
        _localSettings.Values[KeyLanguageCommands] = jsonString;
    }

    /// <summary>
    /// 创建包含所有支持语言默认执行指令的字典。
    /// </summary>
    /// <returns>包含默认配置的新字典实例。</returns>
    private static Dictionary<string, string> CreateDefaultLanguageCommands()
    {
        return SupportedLanguages.ToDictionary(
            lang => lang,
            GetDefaultLanguageCommand,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 获取指定语言的默认执行指令。
    /// </summary>
    /// <param name="language">已规范化为小写的语言标识符。</param>
    /// <returns>该语言的默认执行指令字符串。</returns>
    private static string GetDefaultLanguageCommand(string language)
    {
        return language switch
        {
            "bat" => "cmd /c",
            "powershell" => "powershell -ExecutionPolicy Bypass -File",
            "pwsh" => "pwsh -ExecutionPolicy Bypass -File",
            "python" => "python",
            "lua" => "lua",
            "nim" => "nim r",
            "php" => "php",
            "javascript" => "node",
            "typescript" => "npx ts-node",
            "go" => "go run",
            "vbscript" => "cscript //nologo",
            _ => language
        };
    }

    #endregion
}