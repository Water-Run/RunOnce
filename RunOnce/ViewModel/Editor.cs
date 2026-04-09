/*
 * 代码编辑器页面 ViewModel
 * 管理编辑器页面的光标位置、语言检测结果、命令行参数与执行逻辑
 *
 * @author: WaterRun
 * @file: ViewModel/Editor.cs
 * @date: 2026-04-09
 */

#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using RunOnce.Static;

/// <summary>
/// 代码编辑器页面相关 ViewModel 的语义边界，提供编辑器状态管理与执行调度能力。
/// </summary>
namespace RunOnce.ViewModel;

/// <summary>
/// 代码编辑器页面的 ViewModel，承载光标位置、语言检测结果、命令行参数与执行状态。
/// </summary>
public sealed class EditorViewModel : INotifyPropertyChanged
{
    /// <summary>
    /// 当前光标行号的后备存储；由 <see cref="UpdateCursorPosition"/> 更新；生命周期与实例相同；非只读，非缓存，需在 UI 线程访问。
    /// </summary>
    private int _currentLine = 1;

    /// <summary>
    /// 当前光标列号的后备存储；由 <see cref="UpdateCursorPosition"/> 更新；生命周期与实例相同；非只读，非缓存，需在 UI 线程访问。
    /// </summary>
    private int _currentColumn = 1;

    /// <summary>
    /// 自动检测到的语言标识符的后备存储；由 <see cref="RunDetection"/> 写入；空字符串表示未检测到；生命周期与实例相同；非只读，非缓存，需在 UI 线程访问。
    /// </summary>
    private string _detectedLanguage = string.Empty;

    /// <summary>
    /// 自动检测到的最高置信度的后备存储；由 <see cref="RunDetection"/> 写入；范围 [0, 1]，0 表示未检测到或初始状态；生命周期与实例相同；非只读，非缓存，需在 UI 线程访问。
    /// </summary>
    private double _detectedConfidence;

    /// <summary>
    /// 所有语言检测结果列表的后备存储；由 <see cref="RunDetection"/> 写入；生命周期与实例相同；非只读，非缓存，需在 UI 线程访问。
    /// </summary>
    private IReadOnlyList<DetectionResult> _detectionResults = [];

    /// <summary>
    /// 用户手动指定的语言标识符的后备存储；null 表示使用自动检测结果；生命周期与实例相同；非只读，可为 null，需在 UI 线程访问。
    /// </summary>
    private string? _manualLanguage;

    /// <summary>
    /// 命令行参数字符串的后备存储；仅保存于内存中，不持久化；生命周期与实例相同；非只读，非缓存，需在 UI 线程访问。
    /// </summary>
    private string _commandLineArguments = string.Empty;

    /// <summary>属性值变更时触发，用于通知绑定层刷新对应 UI。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    #region 光标位置属性

    /// <summary>获取当前光标所在行号（从 1 开始）。</summary>
    /// <value>行号整数，最小值为 1；默认值为 1；只读（仅由 <see cref="UpdateCursorPosition"/> 间接更新）；非延迟计算。</value>
    public int CurrentLine
    {
        get => _currentLine;
        private set => SetProperty(ref _currentLine, value);
    }

    /// <summary>获取当前光标所在列号（从 1 开始）。</summary>
    /// <value>列号整数，最小值为 1；默认值为 1；只读（仅由 <see cref="UpdateCursorPosition"/> 间接更新）；非延迟计算。</value>
    public int CurrentColumn
    {
        get => _currentColumn;
        private set => SetProperty(ref _currentColumn, value);
    }

    /// <summary>获取本地化的光标位置显示文本。</summary>
    /// <value>格式为"行 N, 列 M"的本地化字符串；每次访问时重新计算；不允许为 null。</value>
    public string PositionDisplay => $"{Text.Localize("行")} {_currentLine}, {Text.Localize("列")} {_currentColumn}";

    #endregion

    #region 语言检测属性

    /// <summary>获取语言检测结果的本地化显示文本。</summary>
    /// <value>
    /// 手动指定时返回大写语言标识；检测成功时返回"语言 (置信度%)"格式；
    /// 未检测到时返回本地化"纯文本"；每次访问时重新计算；不允许为 null。
    /// </value>
    public string DetectedLanguageDisplay
    {
        get
        {
            if (!string.IsNullOrEmpty(_manualLanguage))
            {
                return _manualLanguage.ToUpperInvariant();
            }

            if (string.IsNullOrEmpty(_detectedLanguage) || _detectedConfidence <= 0)
            {
                return Text.Localize("纯文本");
            }

            return $"{_detectedLanguage.ToUpperInvariant()} ({_detectedConfidence:P0})";
        }
    }

    /// <summary>获取当前生效的语言标识符。</summary>
    /// <value>若已手动指定则返回手动值，否则返回自动检测值；可为空字符串（未检测到时）；不允许为 null；每次访问时重新计算。</value>
    public string EffectiveLanguage => !string.IsNullOrEmpty(_manualLanguage) ? _manualLanguage : _detectedLanguage;

    /// <summary>获取自动检测的最高置信度。</summary>
    /// <value>浮点数，范围 [0, 1]；0 表示未检测到或初始状态；默认值为 0；只读。</value>
    public double DetectedConfidence => _detectedConfidence;

    /// <summary>获取所有语言的检测结果列表。</summary>
    /// <value>按置信度降序排列的只读结果列表；默认为空列表；不允许为 null；只读。</value>
    public IReadOnlyList<DetectionResult> DetectionResults => _detectionResults;

    /// <summary>获取当前检测结果是否达到可信标准。</summary>
    /// <value>当 <see cref="DetectedConfidence"/> 大于等于 <see cref="Config.ConfidenceThreshold"/> 时为 true；每次访问时重新计算。</value>
    public bool IsConfident => _detectedConfidence >= Config.ConfidenceThreshold;

    /// <summary>根据配置判断执行前是否应显示语言选择框。</summary>
    /// <value>true 表示应显示语言选择框；由 <see cref="Config.SelectorMode"/> 与检测结果共同决定；每次访问时重新计算。</value>
    public bool ShouldShowLanguageSelector => Config.SelectorMode switch
    {
        LanguageSelectorMode.AlwaysShow => true,
        LanguageSelectorMode.AutoHide => !IsConfident || string.IsNullOrEmpty(EffectiveLanguage),
        _ => true,
    };

    /// <summary>获取或设置用户手动指定的语言标识符。</summary>
    /// <value>null 表示使用自动检测结果；非 null 时优先于自动检测；默认值为 null；允许为 null。</value>
    public string? ManualLanguage
    {
        get => _manualLanguage;
        set
        {
            if (SetProperty(ref _manualLanguage, value))
            {
                OnPropertyChanged(nameof(DetectedLanguageDisplay));
                OnPropertyChanged(nameof(EffectiveLanguage));
            }
        }
    }

    #endregion

    #region 命令行参数

    /// <summary>获取或设置传递给脚本的命令行参数。</summary>
    /// <value>参数字符串；null 赋值自动替换为空字符串；默认值为空字符串；仅保存于内存中，不持久化；不允许为 null。</value>
    public string CommandLineArguments
    {
        get => _commandLineArguments;
        set
        {
            if (SetProperty(ref _commandLineArguments, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasCommandLineArguments));
            }
        }
    }

    /// <summary>获取当前是否已设置非空的命令行参数。</summary>
    /// <value>true 表示 <see cref="CommandLineArguments"/> 非空白；每次访问时重新计算。</value>
    public bool HasCommandLineArguments => !string.IsNullOrWhiteSpace(_commandLineArguments);

    #endregion

    #region 工作目录

    /// <summary>获取或设置脚本执行的工作目录。</summary>
    /// <value>目录路径字符串；不允许为 null；默认值为 <see cref="Environment.CurrentDirectory"/>。</value>
    public string WorkingDirectory { get; set; } = Environment.CurrentDirectory;

    #endregion

    #region 公开方法

    /// <summary>
    /// 根据文本和字符偏移量更新光标行列信息。
    /// </summary>
    /// <param name="text">RichEditBox 中的原始文本（\r 作为换行符）；允许为 null 或空（将重置为初始位置）。</param>
    /// <param name="charIndex">光标的字符偏移量；允许为负数（将视为无效并重置为初始位置）；超出文本长度时截断至末尾。</param>
    public void UpdateCursorPosition(string text, int charIndex)
    {
        if (string.IsNullOrEmpty(text) || charIndex < 0)
        {
            CurrentLine = 1;
            CurrentColumn = 1;
            OnPropertyChanged(nameof(PositionDisplay));
            return;
        }

        int line = 1;
        int column = 1;
        int safeIndex = Math.Min(charIndex, text.Length);

        for (int i = 0; i < safeIndex; i++)
        {
            if (text[i] == '\r')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        CurrentLine = line;
        CurrentColumn = column;
        OnPropertyChanged(nameof(PositionDisplay));
    }

    /// <summary>
    /// 对代码执行渐进式语言检测并更新所有相关属性。
    /// </summary>
    /// <param name="code">待检测的代码文本（已规范化为 \n 换行）；允许为 null 或空白（将重置检测结果为零置信度）。</param>
    /// <remarks>
    /// 渐进式策略：从前 N 个字符开始检测，若置信度超过阈值则提前停止，
    /// 否则逐步扩大分析范围直至达到最大字符数限制。
    /// 参数 N 由 <see cref="Config.DetectionInitialChars"/>、
    /// <see cref="Config.DetectionIncrementChars"/> 和 <see cref="Config.DetectionMaxChars"/> 控制。
    /// </remarks>
    public void RunDetection(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            _detectionResults = Config.SupportedLanguages
                .Select(lang => new DetectionResult(lang, 0.0))
                .ToList();
            _detectedLanguage = string.Empty;
            _detectedConfidence = 0;
            NotifyDetectionPropertiesChanged();
            return;
        }

        int initialChars = Config.DetectionInitialChars;
        int incrementChars = Config.DetectionIncrementChars;
        int maxChars = Config.DetectionMaxChars;
        double threshold = Config.ConfidenceThreshold;

        int analyzeLength = Math.Min(initialChars, code.Length);
        IReadOnlyList<DetectionResult> lastResults = [];
        DetectionResult lastTop = default;

        while (analyzeLength <= code.Length)
        {
            string snippet = code[..analyzeLength];
            lastResults = LanguageDetector.Detect(snippet);
            lastTop = lastResults.FirstOrDefault();

            if (lastTop.Confidence >= threshold)
            {
                break;
            }

            if (analyzeLength >= Math.Min(maxChars, code.Length))
            {
                break;
            }

            analyzeLength = Math.Min(analyzeLength + incrementChars, Math.Min(maxChars, code.Length));
        }

        _detectionResults = lastResults;

        if (lastTop.Confidence > 0)
        {
            _detectedLanguage = lastTop.Language;
            _detectedConfidence = lastTop.Confidence;
        }
        else
        {
            _detectedLanguage = string.Empty;
            _detectedConfidence = 0;
        }

        NotifyDetectionPropertiesChanged();
    }

    /// <summary>
    /// 执行代码脚本，附带可选的命令行参数与管理员提权。
    /// </summary>
    /// <param name="code">待执行的代码文本；不允许为 null；允许为空白字符串（将提前返回，不执行）；内部将规范化换行符为 \r\n。</param>
    /// <param name="language">目标语言标识符；不允许为 null；允许为空字符串（将提前返回）；应为 <see cref="Config"/> 支持的有效语言标识。</param>
    /// <param name="asAdmin">是否以管理员身份执行；默认为 false。</param>
    /// <exception cref="ArgumentNullException">当 code 或 language 为 null 时抛出。</exception>
    /// <exception cref="ArgumentException">当参数为空白字符串或语言不在支持列表中时抛出。</exception>
    /// <exception cref="IOException">当临时文件创建失败时抛出。</exception>
    /// <exception cref="InvalidOperationException">当终端启动失败时抛出。</exception>
    public void Execute(string code, string language, bool asAdmin = false)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(language);

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrEmpty(language))
        {
            return;
        }

        string normalizedCode = code
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Replace("\n", "\r\n");

        string? arguments = string.IsNullOrWhiteSpace(_commandLineArguments) ? null : _commandLineArguments;

        Exec.Execute(normalizedCode, language, WorkingDirectory, arguments, asAdmin);
    }

    /// <summary>
    /// 刷新所有本地化相关的属性通知。
    /// </summary>
    public void RefreshLocalizedTexts()
    {
        OnPropertyChanged(nameof(DetectedLanguageDisplay));
        OnPropertyChanged(nameof(PositionDisplay));
    }

    #endregion

    #region 私有辅助方法

    /// <summary>
    /// 触发所有检测相关属性的变更通知。
    /// </summary>
    private void NotifyDetectionPropertiesChanged()
    {
        OnPropertyChanged(nameof(DetectedLanguageDisplay));
        OnPropertyChanged(nameof(DetectionResults));
        OnPropertyChanged(nameof(EffectiveLanguage));
        OnPropertyChanged(nameof(IsConfident));
        OnPropertyChanged(nameof(ShouldShowLanguageSelector));
        OnPropertyChanged(nameof(DetectedConfidence));
    }

    #endregion

    #region INotifyPropertyChanged 实现

    /// <summary>触发指定属性名的 <see cref="PropertyChanged"/> 事件，通知绑定层刷新对应 UI。</summary>
    /// <param name="propertyName">属性名称；由编译器通过 <see cref="CallerMemberNameAttribute"/> 自动填充；允许为 null（将触发 null 名称的通知，通常表示所有属性均已变更）。</param>
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>比较后备字段当前值与新值，若不相等则写入新值并触发属性变更通知。</summary>
    /// <typeparam name="T">后备字段与值的类型；需支持 <see cref="EqualityComparer{T}.Default"/> 的相等比较语义。</typeparam>
    /// <param name="field">后备字段的引用；由此方法直接写入新值。</param>
    /// <param name="value">待写入的新值；可为任意 T 类型值（含 null，若 T 为可空类型）。</param>
    /// <param name="propertyName">属性名称；由编译器通过 <see cref="CallerMemberNameAttribute"/> 自动填充；允许为 null。</param>
    /// <returns>若字段值实际发生变化则返回 true；字段值未变化则返回 false。</returns>
    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    #endregion
}
