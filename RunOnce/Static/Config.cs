/*
 * 应用程序配置管理
 * 提供用户设置项与硬编码常量的统一访问入口，支持持久化存储
 *
 * @author: WaterRun
 * @file: Static/Config.cs
 * @date: 2026-03-19
 */

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
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
/// 编辑器性能策略枚举，定义语法高亮与语言检测的资源消耗级别。
/// </summary>
public enum EditorPerformance
{
    /// <summary>低性能消耗，适合低配设备。</summary>
    Low,

    /// <summary>中等性能消耗，默认推荐。</summary>
    Medium,

    /// <summary>高性能消耗，适合高配设备与大型脚本。</summary>
    High,
}

/// <summary>
/// 语言选择框显示模式枚举，定义执行前语言选择框的显示策略。
/// </summary>
public enum LanguageSelectorMode
{
    /// <summary>始终显示语言选择框。</summary>
    AlwaysShow,

    /// <summary>当语言识别可信时自动隐藏选择框（默认行为）。</summary>
    AutoHide,
}

/// <summary>
/// 命令解释器类型枚举，定义执行脚本时使用的 Shell 环境。
/// </summary>
public enum ShellType
{
    /// <summary>PowerShell 5.x（powershell.exe），系统默认编码。</summary>
    PowerShell,

    /// <summary>PowerShell 5.x（powershell.exe），强制 UTF-8 编码。</summary>
    PowerShellUtf8,

    /// <summary>PowerShell 7.x（pwsh.exe），原生 UTF-8。</summary>
    Pwsh,

    /// <summary>命令提示符（cmd.exe），系统默认编码。</summary>
    Cmd,

    /// <summary>命令提示符（cmd.exe），强制 UTF-8 编码。</summary>
    CmdUtf8,
}

/// <summary>
/// 脚本放置行为枚举，定义临时代码文件的放置位置策略。
/// </summary>
public enum ScriptPlacementBehavior
{
    /// <summary>确保清理：将临时文件放置在系统临时目录（默认行为）。</summary>
    EnsureCleanup,

    /// <summary>确保兼容：将临时文件放置在脚本执行的工作目录。</summary>
    EnsureCompatibility,
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
    public const string Version = "1.1.0";

    /// <summary>软件作者名称。</summary>
    /// <value>固定值 "WaterRun"。</value>
    public const string Author = "WaterRun";

    /// <summary>项目 GitHub 仓库地址。</summary>
    /// <value>指向作者的 GitHub 仓库。</value>
    public const string GitHubUrl = "https://github.com/Water-Run/RunOnce";

    /// <summary>微软商店应用链接。</summary>
    /// <value>暂未发布，当前为空字符串。</value>
    public const string MicrosoftStoreUrl = "";

    /// <summary>Windows Terminal 可执行文件名。</summary>
    /// <value>固定值 "wt.exe"。</value>
    public const string WindowsTerminalExecutable = "wt.exe";

    /// <summary>支持的脚本语言列表。</summary>
    /// <value>包含所有可配置执行指令的语言标识符，只读数组。</value>
    public static IReadOnlyList<string> SupportedLanguages { get; } =
    [
        "bat",
        "powershell",
        "python",
        "lua",
        "nim",
        "go",
    ];

    #endregion

    #region 编辑器性能策略参数

    /// <summary>
    /// 获取当前性能策略下语言检测的初始分析字符数。
    /// </summary>
    /// <value>Low=768, Medium=1024, High=1536。</value>
    public static int DetectionInitialChars => Performance switch
    {
        EditorPerformance.Low => 768,
        EditorPerformance.High => 1536,
        _ => 1024,
    };

    /// <summary>
    /// 获取当前性能策略下语言检测每次递增的字符数。
    /// </summary>
    /// <value>Low=384, Medium=512, High=768。</value>
    public static int DetectionIncrementChars => Performance switch
    {
        EditorPerformance.Low => 384,
        EditorPerformance.High => 768,
        _ => 512,
    };

    /// <summary>
    /// 获取当前性能策略下语言检测的最大分析字符数。
    /// </summary>
    /// <value>Low=3072, Medium=4096, High=10240。</value>
    public static int DetectionMaxChars => Performance switch
    {
        EditorPerformance.Low => 3072,
        EditorPerformance.High => 10240,
        _ => 4096,
    };

    /// <summary>
    /// 获取当前性能策略下语法高亮的视窗缓冲区大小（上下各扩展的字符数）。
    /// </summary>
    /// <value>Low=128, Medium=256, High=512。</value>
    public static int HighlightViewportBuffer => Performance switch
    {
        EditorPerformance.Low => 128,
        EditorPerformance.High => 512,
        _ => 256,
    };

    /// <summary>
    /// 获取当前性能策略下编辑器的最大字符数限制。
    /// </summary>
    /// <value>Low=10240, Medium=20480, High=40960。</value>
    public static int MaxCodeLength => Performance switch
    {
        EditorPerformance.Low => 10240,
        EditorPerformance.High => 40960,
        _ => 20480,
    };

    #endregion

    #region 设置项键名常量

    /// <summary>主题风格设置项的存储键名。</summary>
    private const string KeyThemeStyle = "ThemeStyle";

    /// <summary>显示语言设置项的存储键名。</summary>
    private const string KeyDisplayLanguage = "DisplayLanguage";

    /// <summary>编辑器性能策略设置项的存储键名。</summary>
    private const string KeyEditorPerformance = "EditorPerformance";

    /// <summary>语言执行指令配置的存储键名。</summary>
    private const string KeyLanguageCommands = "LanguageCommands";

    /// <summary>临时文件名前缀设置项的存储键名。</summary>
    private const string KeyTempFilePrefix = "TempFilePrefix";

    /// <summary>语言选择框显示模式设置项的存储键名。</summary>
    private const string KeyLanguageSelectorMode = "LanguageSelectorMode";

    /// <summary>执行前确认开关设置项的存储键名。</summary>
    private const string KeyConfirmBeforeExecution = "ConfirmBeforeExecution";

    /// <summary>置信度阈值设置项的存储键名。</summary>
    private const string KeyConfidenceThreshold = "ConfidenceThreshold";

    /// <summary>执行时自动退出开关设置项的存储键名。</summary>
    private const string KeyAutoExitOnExecution = "AutoExitOnExecution";

    /// <summary>运行完毕后自动关闭终端开关设置项的存储键名。</summary>
    private const string KeyAutoCloseTerminalOnCompletion = "AutoCloseTerminalOnCompletion";

    /// <summary>命令解释器类型设置项的存储键名。</summary>
    private const string KeyShellType = "ShellType";

    /// <summary>脚本放置行为设置项的存储键名。</summary>
    private const string KeyScriptPlacement = "ScriptPlacement";

    /// <summary>LLM API Key 设置项的存储键名。</summary>
    private const string KeyLlmApiKey = "LlmApiKey";

    /// <summary>LLM API 基础 URL 设置项的存储键名。</summary>
    private const string KeyLlmBaseUrl = "LlmBaseUrl";

    /// <summary>LLM 模型名称设置项的存储键名。</summary>
    private const string KeyLlmModel = "LlmModel";

    /// <summary>LLM 单次请求最大 Token 数设置项的存储键名。</summary>
    private const string KeyLlmMaxTokens = "LlmMaxTokens";

    /// <summary>LLM 请求超时秒数设置项的存储键名。</summary>
    private const string KeyLlmTimeoutSeconds = "LlmTimeoutSeconds";

    #endregion

    #region 默认值常量

    /// <summary>临时文件名前缀的默认值。</summary>
    public const string DefaultTempFilePrefix = "RunOnce_TMP";

    /// <summary>临时文件名前缀的最大长度。</summary>
    public const int MaxTempFilePrefixLength = 32;

    /// <summary>置信度阈值的默认值。</summary>
    public const double DefaultConfidenceThreshold = 0.85;

    /// <summary>LLM API 基础 URL 的默认值（OpenAI 兼容端点）。</summary>
    public const string DefaultLlmBaseUrl = "https://api.openai.com/v1";

    /// <summary>LLM 模型名称的默认值。</summary>
    public const string DefaultLlmModel = "gpt-4o-mini";

    /// <summary>LLM 单次请求最大 Token 数的默认值。</summary>
    public const int DefaultLlmMaxTokens = 4096;

    /// <summary>LLM 请求超时秒数的默认值。</summary>
    public const int DefaultLlmTimeoutSeconds = 60;

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
    /// 获取或设置编辑器性能策略。
    /// </summary>
    /// <value>
    /// EditorPerformance 枚举值，默认为 Medium（中等）。
    /// 设置时立即持久化到本地存储。
    /// </value>
    public static EditorPerformance Performance
    {
        get
        {
            lock (_syncLock)
            {
                return _localSettings.Values.TryGetValue(KeyEditorPerformance, out object? value)
                       && value is int intValue
                       && Enum.IsDefined(typeof(EditorPerformance), intValue)
                    ? (EditorPerformance)intValue
                    : EditorPerformance.Medium;
            }
        }
        set
        {
            lock (_syncLock)
            {
                _localSettings.Values[KeyEditorPerformance] = (int)value;
            }
        }
    }

    /// <summary>
    /// 获取或设置临时文件名前缀。
    /// </summary>
    /// <value>
    /// 非空字符串，默认为 "RunOnce_TMP"。
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

            if (value.Length > MaxTempFilePrefixLength)
            {
                throw new ArgumentException(
                    Text.Localize("临时文件名前缀长度不能超过 {0} 个字符。", MaxTempFilePrefixLength),
                    nameof(value));
            }

            if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException(Text.Localize("临时文件名前缀不能包含非法文件名字符。"), nameof(value));
            }

            // Directory.EnumerateFiles(pattern) 会将 [] 解析为字符集合，导致匹配语义异常
            if (value.Contains('[') || value.Contains(']'))
            {
                throw new ArgumentException(Text.Localize("临时文件名前缀不能包含字符 '[' 或 ']'。"), nameof(value));
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
    /// LanguageSelectorMode 枚举值，默认为 AutoHide（可信时自动隐藏）。
    /// 若存储中的值无法映射到有效枚举，则回退为默认值。
    /// 设置时立即持久化到本地存储。
    /// </value>
    public static LanguageSelectorMode SelectorMode
    {
        get
        {
            lock (_syncLock)
            {
                return _localSettings.Values.TryGetValue(KeyLanguageSelectorMode, out object? value)
                       && value is int intValue
                       && Enum.IsDefined(typeof(LanguageSelectorMode), intValue)
                    ? (LanguageSelectorMode)intValue
                    : LanguageSelectorMode.AutoHide;
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
                return _localSettings.Values.TryGetValue(KeyConfirmBeforeExecution, out object? value)
                       && value is bool boolValue
                       && boolValue;
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
    /// 获取或设置开始执行代码时是否自动退出应用程序。
    /// </summary>
    /// <value>
    /// 布尔值，true 表示执行时自动退出，false 表示保持运行。默认为 false（关闭）。
    /// 设置时立即持久化到本地存储。
    /// </value>
    public static bool AutoExitOnExecution
    {
        get
        {
            lock (_syncLock)
            {
                return _localSettings.Values.TryGetValue(KeyAutoExitOnExecution, out object? value)
                       && value is bool boolValue
                       && boolValue;
            }
        }
        set
        {
            lock (_syncLock)
            {
                _localSettings.Values[KeyAutoExitOnExecution] = value;
            }
        }
    }

    /// <summary>
    /// 获取或设置代码运行完成后是否自动关闭终端窗口。
    /// </summary>
    /// <value>
    /// 布尔值，true 表示执行完成后自动关闭终端，false 表示保留终端并等待用户确认。默认为 false（关闭）。
    /// 设置时立即持久化到本地存储。
    /// </value>
    public static bool AutoCloseTerminalOnCompletion
    {
        get
        {
            lock (_syncLock)
            {
                return _localSettings.Values.TryGetValue(KeyAutoCloseTerminalOnCompletion, out object? value)
                       && value is bool boolValue
                       && boolValue;
            }
        }
        set
        {
            lock (_syncLock)
            {
                _localSettings.Values[KeyAutoCloseTerminalOnCompletion] = value;
            }
        }
    }

    /// <summary>
    /// 获取或设置执行脚本时使用的命令解释器类型。
    /// </summary>
    /// <value>
    /// ShellType 枚举值，默认为 PowerShellUtf8（powershell.exe 5.x，强制 UTF-8）。
    /// 设置时立即持久化到本地存储。
    /// </value>
    public static ShellType Shell
    {
        get
        {
            lock (_syncLock)
            {
                return _localSettings.Values.TryGetValue(KeyShellType, out object? value)
                       && value is int intValue
                       && Enum.IsDefined(typeof(ShellType), intValue)
                    ? (ShellType)intValue
                    : ShellType.PowerShellUtf8;
            }
        }
        set
        {
            lock (_syncLock)
            {
                _localSettings.Values[KeyShellType] = (int)value;
            }
        }
    }

    /// <summary>
    /// 获取或设置脚本临时文件的放置行为。
    /// </summary>
    /// <value>
    /// ScriptPlacementBehavior 枚举值，默认为 EnsureCleanup（放置在系统临时目录）。
    /// 设置时立即持久化到本地存储。
    /// </value>
    public static ScriptPlacementBehavior ScriptPlacement
    {
        get
        {
            lock (_syncLock)
            {
                return _localSettings.Values.TryGetValue(KeyScriptPlacement, out object? value)
                       && value is int intValue
                       && Enum.IsDefined(typeof(ScriptPlacementBehavior), intValue)
                    ? (ScriptPlacementBehavior)intValue
                    : ScriptPlacementBehavior.EnsureCleanup;
            }
        }
        set
        {
            lock (_syncLock)
            {
                _localSettings.Values[KeyScriptPlacement] = (int)value;
            }
        }
    }

    /// <summary>
    /// 获取或设置语言识别的置信度阈值。
    /// </summary>
    /// <value>
    /// 范围 [0.0, 1.0]，默认为 0.85。
    /// 高于此值判定为可信，低于此值判定为不可信。
    /// 若存储中的值超出有效范围则回退为默认值。
    /// </value>
    /// <exception cref="ArgumentOutOfRangeException">当设置值超出 [0.0, 1.0] 范围时抛出。</exception>
    public static double ConfidenceThreshold
    {
        get
        {
            lock (_syncLock)
            {
                return _localSettings.Values.TryGetValue(KeyConfidenceThreshold, out object? value)
                       && value is double doubleValue
                       && doubleValue >= 0.0
                       && doubleValue <= 1.0
                    ? doubleValue
                    : DefaultConfidenceThreshold;
            }
        }
        set
        {
            if (value is < 0.0 or > 1.0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    Text.Localize("置信度阈值必须在 [0.0, 1.0] 范围内。"));
            }

            lock (_syncLock)
            {
                _localSettings.Values[KeyConfidenceThreshold] = value;
            }
        }
    }

    /// <summary>
    /// 获取或设置 LLM API Key。
    /// </summary>
    /// <value>
    /// 字符串，默认为空。未配置时 LlmClient 将拒绝发起请求。
    /// 设置时立即持久化到本地存储。
    /// </value>
    public static string LlmApiKey
    {
        get
        {
            lock (_syncLock)
            {
                return _localSettings.Values.TryGetValue(KeyLlmApiKey, out object? value)
                       && value is string stringValue
                    ? stringValue
                    : string.Empty;
            }
        }
        set
        {
            lock (_syncLock)
            {
                _localSettings.Values[KeyLlmApiKey] = value ?? string.Empty;
            }
        }
    }

    /// <summary>
    /// 获取或设置 LLM API 基础 URL（OpenAI 兼容端点）。
    /// </summary>
    /// <value>
    /// 字符串，默认为 "https://api.openai.com/v1"。
    /// 设置时立即持久化到本地存储。
    /// </value>
    public static string LlmBaseUrl
    {
        get
        {
            lock (_syncLock)
            {
                return _localSettings.Values.TryGetValue(KeyLlmBaseUrl, out object? value)
                       && value is string stringValue
                       && !string.IsNullOrWhiteSpace(stringValue)
                    ? stringValue
                    : DefaultLlmBaseUrl;
            }
        }
        set
        {
            lock (_syncLock)
            {
                _localSettings.Values[KeyLlmBaseUrl] = string.IsNullOrWhiteSpace(value)
                    ? DefaultLlmBaseUrl
                    : value;
            }
        }
    }

    /// <summary>
    /// 获取或设置 LLM 模型名称。
    /// </summary>
    /// <value>
    /// 字符串，默认为 "gpt-4o-mini"。
    /// 设置时立即持久化到本地存储。
    /// </value>
    public static string LlmModel
    {
        get
        {
            lock (_syncLock)
            {
                return _localSettings.Values.TryGetValue(KeyLlmModel, out object? value)
                       && value is string stringValue
                       && !string.IsNullOrWhiteSpace(stringValue)
                    ? stringValue
                    : DefaultLlmModel;
            }
        }
        set
        {
            lock (_syncLock)
            {
                _localSettings.Values[KeyLlmModel] = string.IsNullOrWhiteSpace(value)
                    ? DefaultLlmModel
                    : value;
            }
        }
    }

    /// <summary>
    /// 获取或设置 LLM 单次请求允许生成的最大 Token 数。
    /// </summary>
    /// <value>
    /// 正整数，默认为 4096。
    /// 设置时立即持久化到本地存储。
    /// </value>
    public static int LlmMaxTokens
    {
        get
        {
            lock (_syncLock)
            {
                return _localSettings.Values.TryGetValue(KeyLlmMaxTokens, out object? value)
                       && value is int intValue
                       && intValue > 0
                    ? intValue
                    : DefaultLlmMaxTokens;
            }
        }
        set
        {
            lock (_syncLock)
            {
                _localSettings.Values[KeyLlmMaxTokens] = value > 0 ? value : DefaultLlmMaxTokens;
            }
        }
    }

    /// <summary>
    /// 获取或设置 LLM 请求的超时秒数。
    /// </summary>
    /// <value>
    /// 正整数，默认为 60。
    /// 设置时立即持久化到本地存储。
    /// </value>
    public static int LlmTimeoutSeconds
    {
        get
        {
            lock (_syncLock)
            {
                return _localSettings.Values.TryGetValue(KeyLlmTimeoutSeconds, out object? value)
                       && value is int intValue
                       && intValue > 0
                    ? intValue
                    : DefaultLlmTimeoutSeconds;
            }
        }
        set
        {
            lock (_syncLock)
            {
                _localSettings.Values[KeyLlmTimeoutSeconds] = value > 0 ? value : DefaultLlmTimeoutSeconds;
            }
        }
    }

    #endregion

    #region 语言指令配置

    /// <summary>
    /// 获取指定脚本语言的执行指令。
    /// </summary>
    /// <param name="language">
    /// 脚本语言标识符，必须是 <see cref="SupportedLanguages"/> 中定义的有效值，不区分大小写。
    /// 不允许为 null 或空白字符串。
    /// </param>
    /// <returns>该语言对应的执行指令字符串；若未配置则返回该语言的默认指令。</returns>
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
    /// 脚本语言标识符，必须是 <see cref="SupportedLanguages"/> 中定义的有效值，不区分大小写。
    /// 不允许为 null 或空白字符串。
    /// </param>
    /// <param name="command">执行指令字符串，不允许为 null 或空白字符串。</param>
    /// <exception cref="ArgumentNullException">当 language 或 command 为 null 时抛出。</exception>
    /// <exception cref="ArgumentException">当参数为空白字符串或 language 不在支持列表中时抛出。</exception>
    /// <remarks>设置后立即持久化到本地存储。</remarks>
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
    /// <returns>包含所有已配置语言及其执行指令的字典副本；未配置的语言将使用默认值填充。</returns>
    /// <remarks>返回的是配置数据的深拷贝，修改返回值不会影响内部存储。</remarks>
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
    /// 脚本语言标识符，必须是 <see cref="SupportedLanguages"/> 中定义的有效值，不区分大小写。
    /// 不允许为 null 或空白字符串。
    /// </param>
    /// <exception cref="ArgumentNullException">当 language 为 null 时抛出。</exception>
    /// <exception cref="ArgumentException">当 language 为空白字符串或不在支持列表中时抛出。</exception>
    /// <remarks>重置后立即持久化到本地存储。</remarks>
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
    /// <remarks>重置后立即持久化到本地存储。</remarks>
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
    public static string GetThemeDisplayName(ThemeStyle theme) => theme switch
    {
        ThemeStyle.FollowSystem => Text.Localize("跟随系统"),
        ThemeStyle.Light => Text.Localize("浅色"),
        ThemeStyle.Dark => Text.Localize("深色"),
        _ => theme.ToString(),
    };

    /// <summary>
    /// 获取显示语言枚举值的本地化显示名称。
    /// </summary>
    /// <param name="language">显示语言枚举值。</param>
    /// <returns>本地化后的显示名称字符串。</returns>
    public static string GetLanguageDisplayName(DisplayLanguage language) => language switch
    {
        DisplayLanguage.FollowSystem => Text.Localize("跟随系统"),
        DisplayLanguage.Chinese => Text.Localize("简体中文"),
        DisplayLanguage.English => Text.Localize("English"),
        _ => language.ToString(),
    };

    /// <summary>
    /// 获取编辑器性能策略枚举值的本地化显示名称。
    /// </summary>
    /// <param name="performance">编辑器性能策略枚举值。</param>
    /// <returns>本地化后的显示名称字符串。</returns>
    public static string GetPerformanceDisplayName(EditorPerformance performance) => performance switch
    {
        EditorPerformance.Low => Text.Localize("低"),
        EditorPerformance.Medium => Text.Localize("中等"),
        EditorPerformance.High => Text.Localize("高"),
        _ => performance.ToString(),
    };

    /// <summary>
    /// 获取语言选择器模式枚举值的本地化显示名称。
    /// </summary>
    /// <param name="mode">语言选择器模式枚举值。</param>
    /// <returns>本地化后的显示名称字符串。</returns>
    public static string GetSelectorModeDisplayName(LanguageSelectorMode mode) => mode switch
    {
        LanguageSelectorMode.AlwaysShow => Text.Localize("始终显示"),
        LanguageSelectorMode.AutoHide => Text.Localize("自动隐藏"),
        _ => mode.ToString(),
    };

    /// <summary>
    /// 获取命令解释器类型枚举值的本地化显示名称。
    /// </summary>
    /// <param name="shell">命令解释器类型枚举值。</param>
    /// <returns>本地化后的显示名称字符串。</returns>
    public static string GetShellDisplayName(ShellType shell) => shell switch
    {
        ShellType.PowerShell => "PowerShell",
        ShellType.PowerShellUtf8 => "PowerShell (UTF-8)",
        ShellType.Pwsh => "Pwsh (PowerShell 7)",
        ShellType.Cmd => Text.Localize("命令提示符"),
        ShellType.CmdUtf8 => Text.Localize("命令提示符") + " (UTF-8)",
        _ => shell.ToString(),
    };

    /// <summary>
    /// 获取脚本放置行为枚举值的本地化显示名称。
    /// </summary>
    /// <param name="placement">脚本放置行为枚举值。</param>
    /// <returns>本地化后的显示名称字符串。</returns>
    public static string GetScriptPlacementDisplayName(ScriptPlacementBehavior placement) => placement switch
    {
        ScriptPlacementBehavior.EnsureCleanup => Text.Localize("确保清理"),
        ScriptPlacementBehavior.EnsureCompatibility => Text.Localize("确保兼容"),
        _ => placement.ToString(),
    };

    #endregion

    #region 重置方法

    /// <summary>
    /// 将所有用户设置项重置为默认值。
    /// </summary>
    /// <remarks>
    /// 包括主题风格、显示语言、编辑器性能策略、临时文件前缀、语言选择框模式、执行确认开关、
    /// 置信度阈值、执行时自动退出、执行完成自动关闭终端、命令解释器类型、
    /// 脚本放置行为以及所有语言执行指令。重置后立即持久化到本地存储。
    /// </remarks>
    public static void ResetAllSettings()
    {
        lock (_syncLock)
        {
            _localSettings.Values[KeyThemeStyle] = (int)ThemeStyle.FollowSystem;
            _localSettings.Values[KeyDisplayLanguage] = (int)DisplayLanguage.FollowSystem;
            _localSettings.Values[KeyEditorPerformance] = (int)EditorPerformance.Medium;
            _localSettings.Values[KeyTempFilePrefix] = DefaultTempFilePrefix;
            _localSettings.Values[KeyLanguageSelectorMode] = (int)LanguageSelectorMode.AutoHide;
            _localSettings.Values[KeyConfirmBeforeExecution] = false;
            _localSettings.Values[KeyConfidenceThreshold] = DefaultConfidenceThreshold;
            _localSettings.Values[KeyAutoExitOnExecution] = false;
            _localSettings.Values[KeyAutoCloseTerminalOnCompletion] = false;
            _localSettings.Values[KeyShellType] = (int)ShellType.PowerShellUtf8;
            _localSettings.Values[KeyScriptPlacement] = (int)ScriptPlacementBehavior.EnsureCleanup;
            _localSettings.Values[KeyLlmApiKey] = string.Empty;
            _localSettings.Values[KeyLlmBaseUrl] = DefaultLlmBaseUrl;
            _localSettings.Values[KeyLlmModel] = DefaultLlmModel;
            _localSettings.Values[KeyLlmMaxTokens] = DefaultLlmMaxTokens;
            _localSettings.Values[KeyLlmTimeoutSeconds] = DefaultLlmTimeoutSeconds;
            _languageCommands = CreateDefaultLanguageCommands();
            _languageCommandsLoaded = true;
            PersistLanguageCommands();
        }
    }

    #endregion

    #region 私有辅助方法

    /// <summary>
    /// 确保语言指令配置已从存储加载到内存缓存。
    /// </summary>
    /// <remarks>
    /// 必须在持有 <see cref="_syncLock"/> 的情况下调用。
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
                // CFG-001: 配置数据损坏时回退到默认值，保证应用可正常启动
            }
        }

        _languageCommands = CreateDefaultLanguageCommands();
        _languageCommandsLoaded = true;
    }

    /// <summary>
    /// 将当前内存中的语言指令配置持久化到本地存储。
    /// </summary>
    /// <remarks>
    /// 必须在持有 <see cref="_syncLock"/> 的情况下调用。
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
    private static string GetDefaultLanguageCommand(string language) => language switch
    {
        "bat" => "cmd /c",
        "powershell" => "powershell -ExecutionPolicy Bypass -File",
        "python" => "python",
        "lua" => "lua",
        "nim" => "nim r",
        "go" => "go run",
        _ => language,
    };

    #endregion
}