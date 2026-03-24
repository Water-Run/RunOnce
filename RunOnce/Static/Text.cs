/*
 * 国际化文本管理
 * 提供应用程序界面文本的多语言支持，以中文为基础语言，支持英文翻译
 *
 * @author: WaterRun
 * @file: Static/Text.cs
 * @date: 2026-03-19
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

        // 显示语言
        ["简体中文"] = "简体中文",
        ["English"] = "English",

        // 编辑器性能策略
        ["低"] = "Low",
        ["中等"] = "Medium",
        ["高"] = "High",

        // 语言选择器模式
        ["始终显示"] = "Always Show",
        ["自动隐藏"] = "Auto Hide",

        // 终端类型
        ["Windows 终端"] = "Windows Terminal",
        ["命令提示符"] = "Command Prompt",

        // 脚本放置行为
        ["确保清理"] = "Ensure Cleanup",
        ["确保兼容"] = "Ensure Compatibility",

        // 页面标题
        ["编辑器"] = "Editor",
        ["设置"] = "Settings",

        // 设置页面 - 分组标题
        ["基本"] = "General",
        ["代码执行"] = "Code Execution",
        ["此程序"] = "About",

        // 设置页面 - 基本设置
        ["外观"] = "Appearance",
        ["选择应用程序的主题风格"] = "Choose the application theme style",
        ["语言"] = "Language",
        ["选择应用程序的显示语言"] = "Choose the application display language",
        ["编辑器性能"] = "Editor Performance",
        ["调整语法高亮与语言检测的资源消耗级别"] = "Adjust the resource consumption level for syntax highlighting and language detection",
        ["切换性能策略将清空编辑器中的所有内容，确定继续吗？"] = "Switching performance profile will clear all editor content. Continue?",
        ["继续"] = "Continue",

        // 设置页面 - 代码执行设置
        ["执行前确认"] = "Confirm Before Execution",
        ["执行代码前显示确认对话框"] = "Show confirmation dialog before executing code",
        ["执行前语言选择框"] = "Language Selector Before Execution",
        ["控制语言选择框的显示时机"] = "Control when the language selector is displayed",
        ["执行时自动退出"] = "Auto Exit On Execute",
        ["开始执行代码后自动关闭应用程序"] = "Automatically close the application when execution starts",
        ["运行完毕后自动关闭终端"] = "Auto close terminal on completion",
        ["代码运行完成后自动关闭终端窗口"] = "Automatically close the terminal window when code execution completes",
        ["终端类型"] = "Terminal Type",
        ["选择启动的终端模拟器"] = "Choose the terminal emulator to launch",
        ["运行环境"] = "Shell",
        ["选择执行代码使用的命令解释器"] = "Choose the shell for code execution",
        ["脚本放置行为"] = "Script Placement",
        ["选择临时代码文件的放置位置"] = "Choose where temporary code files are placed",
        ["高级设置"] = "Advanced Settings",
        ["配置临时文件、置信度阈值和语言命令"] = "Configure the temporary file, confidence threshold, and language commands",
        ["打开"] = "Open",

        // 设置页面 - 快捷键
        ["快捷键"] = "Keyboard Shortcuts",
        ["查看应用程序支持的快捷键"] = "View supported keyboard shortcuts",
        ["查看"] = "View",
        ["关闭"] = "Close",
        ["执行代码"] = "Execute Code",
        ["撤销"] = "Undo",
        ["重做"] = "Redo",
        ["全选"] = "Select All",
        ["复制"] = "Copy",
        ["粘贴"] = "Paste",
        ["剪切"] = "Cut",
        ["缩进"] = "Indent",
        ["减少缩进"] = "Unindent",

        // 设置页面 - 关于
        ["软件名"] = "App Name",
        ["编译于"] = "Built On",
        ["微软商店"] = "Microsoft Store",
        ["访问"] = "Visit",
        ["重置所有设置"] = "Reset All Settings",

        // 高级设置对话框
        ["临时文件名前缀"] = "Temporary File Prefix",
        ["置信度阈值"] = "Confidence Threshold",
        ["语言执行命令"] = "Language Execution Commands",
        ["重置为默认"] = "Reset to Default",
        ["保存"] = "Save",
        ["取消"] = "Cancel",

        // 重置确认对话框
        ["确定要将所有设置重置为默认值吗？此操作无法撤销。"] = "Are you sure you want to reset all settings to default? This action cannot be undone.",
        ["重置"] = "Reset",

        // 脚本放置行为确认对话框
        ["此操作将把临时代码文件放置在工作目录，当异常关闭时，可能无法有效的清理。"] = "This will place temporary code files in the working directory. They may not be cleaned up properly if the application closes unexpectedly.",
        ["此操作将把临时代码文件放置在临时目录，可能产生一些兼容性问题。"] = "This will place temporary code files in the temp directory, which may cause some compatibility issues.",

        // 编辑器页面
        ["在此粘贴代码"] = "Paste code here",
        ["右键或Ctrl+V"] = "Right-click or Ctrl+V",
        ["纯文本"] = "Plain Text",
        ["行"] = "Ln",
        ["列"] = "Col",
        ["运行"] = "Run",
        ["选择语言"] = "Select Language",
        ["自动检测"] = "Auto Detect",
        ["确定"] = "OK",
        ["执行"] = "Execute",
        ["确定要执行此代码吗？"] = "Are you sure you want to execute this code?",
        ["超出输入限制"] = "Input Limit Exceeded",
        ["你的内容超过最大长度限制, 超出部分已被截断."] = "Your content exceeds the maximum length limit. The excess has been truncated.",
        ["\"一次运行\"面向的是\"一次性\"的脚本, 当代码达到这个长度时, 你最好需要好好看看, 而不是直接粘贴运行."] = "RunOnce is designed for \"one-time\" scripts. When your code reaches this length, you'd better review it carefully rather than just paste and run.",

        // 命令行参数
        ["命令行参数"] = "Command Line Arguments",
        ["清除"] = "Clear",

        // 终端执行提示
        ["按 Enter 键退出"] = "Press Enter to exit",
        [":一次运行:"] = ":RunOnce:",
        [":一次运行: 运行于兼容模式. 完成后记得使用回车安全清理"] = ":RunOnce: Running in compatibility mode. Press Enter at the end for safe cleanup.",
        [":一次运行: 摁下回车继续>>>"] = ":RunOnce: Press Enter to continue>>>",
        [":一次运行: 摁下回车继续(否则无法清理)>>>"] = ":RunOnce: Press Enter to continue (otherwise cleanup will fail)>>>",

        // Config.cs 中的错误消息
        ["置信度阈值必须在 [0.0, 1.0] 范围内。"] = "Confidence threshold must be in the range [0.0, 1.0].",
        ["临时文件名前缀不能为空白字符串。"] = "Temporary file prefix cannot be empty or whitespace.",
        ["临时文件名前缀长度不能超过 {0} 个字符。"] = "Temporary file prefix cannot exceed {0} characters.",
        ["临时文件名前缀不能包含非法文件名字符。"] = "Temporary file prefix cannot contain invalid file name characters.",
        ["临时文件名前缀不能包含字符 '[' 或 ']'。"] = "Temporary file prefix cannot contain '[' or ']'.",
        ["语言标识符不能为空白字符串。"] = "Language identifier cannot be empty or whitespace.",
        ["不支持的语言标识符: {0}。"] = "Unsupported language identifier: {0}.",
        ["执行指令不能为空白字符串。"] = "Execution command cannot be empty or whitespace.",

        // LanguageDetector.cs 中的错误消息
        ["结果数量必须大于 0。"] = "Result count must be greater than 0.",

        // Exec.cs 中的错误消息
        ["代码内容不能为空。"] = "Code content cannot be empty.",
        ["工作目录不能为空白字符串。"] = "Working directory cannot be empty or whitespace.",
        ["工作目录不存在: {0}。"] = "Working directory does not exist: {0}.",
        ["无法创建临时文件: {0}。"] = "Failed to create temporary file: {0}.",
        ["无法启动终端进程。"] = "Failed to start terminal process.",

        // LLM 功能
        ["AI 设置"] = "AI Settings",
        ["配置 LLM API 以生成脚本代码"] = "Configure LLM API to generate script code",
        ["AI 生成代码"] = "AI Generate Code",
        ["描述你的需求"] = "Describe your requirements",
        ["例如：列出当前目录下所有.txt文件"] = "e.g.: List all .txt files in current directory",
        ["生成"] = "Generate",
        ["自动"] = "Auto",
        ["AI 生成"] = "AI Generate",
        ["正在生成..."] = "Generating...",

        // LLM 设置对话框
        ["API Key"] = "API Key",
        ["输入 API Key"] = "Enter API Key",
        ["API 基础 URL"] = "API Base URL",
        ["模型名称"] = "Model Name",
        ["最大 Token 数"] = "Max Tokens",
        ["请求超时（秒）"] = "Request Timeout (s)",

        // LLM 错误消息
        ["需求描述不能为空。"] = "Requirements description cannot be empty.",
        ["尚未配置 API Key，请在设置中配置 LLM API Key。"] = "API Key is not configured. Please set the LLM API Key in Settings.",
        ["LLM API 请求超时，请检查网络连接或在设置中增加超时时间。"] = "LLM API request timed out. Please check your network connection or increase the timeout in Settings.",
        ["LLM API 返回错误 ({0}): {1}"] = "LLM API returned error ({0}): {1}",
        ["你是一个专业的脚本生成助手。根据用户的需求，使用 {0} 语言生成可执行的脚本代码。仅输出脚本代码本身，不要包含任何解释、注释说明或 Markdown 代码块标记。"] =
            "You are a professional script generation assistant. Generate executable script code in {0} based on the user's requirements. Output only the script code itself, without any explanations, comments, or Markdown code block markers.",
        ["你是一个专业的脚本生成助手。根据用户的需求生成可执行的脚本代码。仅输出脚本代码本身，不要包含任何解释、注释说明或 Markdown 代码块标记。支持的语言：bat、powershell、python、lua、nim、go。根据需求自动选择最合适的语言。"] =
            "You are a professional script generation assistant. Generate executable script code based on the user's requirements. Output only the script code itself, without any explanations, comments, or Markdown code block markers. Supported languages: bat, powershell, python, lua, nim, go. Automatically choose the most appropriate language.",
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
    /// 当设置为 FollowSystem 时，通过 <see cref="CultureInfo.CurrentUICulture"/> 判断是否为中文环境。
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
    /// <param name="chinese">中文原文模板，支持 <see cref="string.Format(string,object[])"/> 占位符，不允许为 null。</param>
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
    private static bool ShouldUseChinese() => Config.Language switch
    {
        DisplayLanguage.Chinese => true,
        DisplayLanguage.English => false,
        _ => IsSystemChinese(),
    };

    /// <summary>
    /// 判断系统当前 UI 文化是否为中文。
    /// </summary>
    /// <returns>若系统 UI 文化为中文（zh 开头）则返回 true，否则返回 false。</returns>
    private static bool IsSystemChinese()
    {
        return CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 获取当前实际使用的显示语言。
    /// </summary>
    /// <returns>返回实际生效的语言：当设置为 FollowSystem 时返回系统检测结果，否则返回用户设置值。</returns>
    public static DisplayLanguage GetEffectiveLanguage()
    {
        DisplayLanguage language = Config.Language;
        return language == DisplayLanguage.FollowSystem
            ? (IsSystemChinese() ? DisplayLanguage.Chinese : DisplayLanguage.English)
            : language;
    }

    /// <summary>
    /// 检查指定的中文文本是否存在英文翻译。
    /// </summary>
    /// <param name="chinese">待检查的中文原文，不允许为 null。</param>
    /// <returns>若存在英文翻译则返回 true，否则返回 false。</returns>
    /// <exception cref="ArgumentNullException">当 chinese 为 null 时抛出。</exception>
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