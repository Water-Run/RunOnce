/*
 * 设置页面 ViewModel
 * 管理设置页面的所有可绑定状态、选项列表与业务逻辑，向 View 暴露配置数据的读写接口
 *
 * @author: WaterRun
 * @file: ViewModel/Settings.cs
 * @date: 2026-03-24
 */

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using RunOnce.Static;

/// <summary>
/// 设置页面相关 ViewModel 的语义边界，提供设置状态管理与配置持久化能力。
/// </summary>
namespace RunOnce.ViewModel;

/// <summary>
/// 设置页面的 ViewModel，承载所有用户可交互设置的绑定状态、选项列表与关于信息。
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    /// <summary>
    /// 应用程序编译时间戳的静态缓存；由 <see cref="RetrieveBuildTime"/> 在类型加载时初始化；生命周期与应用域相同；只读，惰性初始化于类型加载时。
    /// </summary>
    private static readonly DateTime _buildTime = RetrieveBuildTime();

    /// <summary>
    /// 程序化批量更新期间的抑制标志后备存储；置为 true 时所有属性 setter 跳过 Config 回写与业务事件触发；生命周期与实例相同；非只读，非缓存，需在 UI 线程访问。
    /// </summary>
    private bool _isSuppressingChanges;

    #region 选项列表（ObservableCollection 实现原地更新）

    /// <summary>
    /// 主题风格选项文本列表的后备存储；元素与 <see cref="ThemeStyle"/> 枚举值一一对应；由构造函数初始化，由 <see cref="RefreshOptionTexts"/> 原地更新；生命周期与实例相同；只读引用，内容非只读，需在 UI 线程访问。
    /// </summary>
    private readonly ObservableCollection<string> _themeOptions;

    /// <summary>
    /// 显示语言选项文本列表的后备存储；元素与 <see cref="DisplayLanguage"/> 枚举值一一对应；由构造函数初始化，由 <see cref="RefreshOptionTexts"/> 原地更新；生命周期与实例相同；只读引用，内容非只读，需在 UI 线程访问。
    /// </summary>
    private readonly ObservableCollection<string> _languageOptions;

    /// <summary>
    /// 编辑器性能策略选项文本列表的后备存储；元素与 <see cref="EditorPerformance"/> 枚举值一一对应；由构造函数初始化，由 <see cref="RefreshOptionTexts"/> 原地更新；生命周期与实例相同；只读引用，内容非只读，需在 UI 线程访问。
    /// </summary>
    private readonly ObservableCollection<string> _performanceOptions;

    /// <summary>
    /// 语言选择框模式选项文本列表的后备存储；元素与 <see cref="LanguageSelectorMode"/> 枚举值一一对应；由构造函数初始化，由 <see cref="RefreshOptionTexts"/> 原地更新；生命周期与实例相同；只读引用，内容非只读，需在 UI 线程访问。
    /// </summary>
    private readonly ObservableCollection<string> _selectorModeOptions;

    /// <summary>
    /// 命令解释器类型选项文本列表的后备存储；元素与 <see cref="ShellType"/> 枚举值一一对应；由构造函数初始化，由 <see cref="RefreshOptionTexts"/> 原地更新；生命周期与实例相同；只读引用，内容非只读，需在 UI 线程访问。
    /// </summary>
    private readonly ObservableCollection<string> _shellOptions;

    /// <summary>
    /// 脚本放置行为选项文本列表的后备存储；元素与 <see cref="ScriptPlacementBehavior"/> 枚举值一一对应；由构造函数初始化，由 <see cref="RefreshOptionTexts"/> 原地更新；生命周期与实例相同；只读引用，内容非只读，需在 UI 线程访问。
    /// </summary>
    private readonly ObservableCollection<string> _scriptPlacementOptions;

    #endregion

    #region 选中索引后备字段

    /// <summary>
    /// 主题风格 ComboBox 选中索引的后备存储；由 <see cref="SelectedThemeIndex"/> 的 setter 更新；范围与 <see cref="ThemeStyle"/> 枚举有效值一致；生命周期与实例相同；非只读，非缓存，需在 UI 线程访问。
    /// </summary>
    private int _selectedThemeIndex;

    /// <summary>
    /// 显示语言 ComboBox 选中索引的后备存储；由 <see cref="SelectedLanguageIndex"/> 的 setter 更新；范围与 <see cref="DisplayLanguage"/> 枚举有效值一致；生命周期与实例相同；非只读，非缓存，需在 UI 线程访问。
    /// </summary>
    private int _selectedLanguageIndex;

    /// <summary>
    /// 编辑器性能策略 ComboBox 选中索引的后备存储；由 <see cref="SelectedPerformanceIndex"/> 的 setter 更新；范围与 <see cref="EditorPerformance"/> 枚举有效值一致；生命周期与实例相同；非只读，非缓存，需在 UI 线程访问。
    /// </summary>
    private int _selectedPerformanceIndex;

    /// <summary>
    /// 语言选择框模式 ComboBox 选中索引的后备存储；由 <see cref="SelectedSelectorModeIndex"/> 的 setter 更新；范围与 <see cref="LanguageSelectorMode"/> 枚举有效值一致；生命周期与实例相同；非只读，非缓存，需在 UI 线程访问。
    /// </summary>
    private int _selectedSelectorModeIndex;

    /// <summary>
    /// 命令解释器类型 ComboBox 选中索引的后备存储；由 <see cref="SelectedShellIndex"/> 的 setter 更新；范围与 <see cref="ShellType"/> 枚举有效值一致；生命周期与实例相同；非只读，非缓存，需在 UI 线程访问。
    /// </summary>
    private int _selectedShellIndex;

    /// <summary>
    /// 脚本放置行为 ComboBox 选中索引的后备存储；由 <see cref="SelectedScriptPlacementIndex"/> 的 setter 更新；范围与 <see cref="ScriptPlacementBehavior"/> 枚举有效值一致；生命周期与实例相同；非只读，非缓存，需在 UI 线程访问。
    /// </summary>
    private int _selectedScriptPlacementIndex;

    #endregion

    #region 开关后备字段

    /// <summary>
    /// 执行前是否显示确认对话框的后备存储；由 <see cref="ConfirmBeforeExecution"/> 的 setter 更新；初始值由 Config 同步；生命周期与实例相同；非只读，非缓存，需在 UI 线程访问。
    /// </summary>
    private bool _confirmBeforeExecution;

    /// <summary>
    /// 执行代码时是否自动退出应用程序的后备存储；由 <see cref="AutoExitOnExecution"/> 的 setter 更新；初始值由 Config 同步；生命周期与实例相同；非只读，非缓存，需在 UI 线程访问。
    /// </summary>
    private bool _autoExitOnExecution;

    /// <summary>
    /// 代码运行完成后是否自动关闭终端的后备存储；由 <see cref="AutoCloseTerminalOnCompletion"/> 的 setter 更新；初始值由 Config 同步；生命周期与实例相同；非只读，非缓存，需在 UI 线程访问。
    /// </summary>
    private bool _autoCloseTerminalOnCompletion;

    #endregion

    #region 事件

    /// <summary>属性值变更时触发，用于通知绑定层刷新对应 UI。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>用户更改主题风格时触发，参数为新选中的 <see cref="ThemeStyle"/> 值；View 层应在此事件中执行资源字典切换等主题应用操作。</summary>
    public event Action<ThemeStyle>? ThemeChanged;

    /// <summary>用户更改显示语言时触发；View 层应重新加载本地化资源后调用 <see cref="RefreshAfterLanguageChange"/> 刷新 ViewModel 状态。</summary>
    public event Action? LanguageChanged;

    /// <summary>用户更改脚本放置行为时触发，参数依次为旧索引与新索引；View 层应弹出确认对话框后调用 <see cref="ConfirmScriptPlacement"/> 或 <see cref="RevertScriptPlacement"/>。</summary>
    public event Action<int, int>? ScriptPlacementChangeRequested;

    /// <summary>用户更改编辑器性能策略时触发，参数依次为旧索引与新索引；View 层应弹出确认对话框后调用 <see cref="ConfirmPerformanceChange"/> 或 <see cref="RevertPerformanceChange"/>。</summary>
    public event Action<int, int>? PerformanceChangeRequested;

    #endregion

    /// <summary>
    /// 初始化设置 ViewModel 实例，填充所有 ComboBox 选项列表并从 <see cref="Config"/> 同步初始状态。
    /// </summary>
    public SettingsViewModel()
    {
        _themeOptions = new(Enum.GetValues<ThemeStyle>().Select(Config.GetThemeDisplayName));
        _languageOptions = new(Enum.GetValues<DisplayLanguage>().Select(Config.GetLanguageDisplayName));
        _performanceOptions = new(Enum.GetValues<EditorPerformance>().Select(Config.GetPerformanceDisplayName));
        _selectorModeOptions = new(Enum.GetValues<LanguageSelectorMode>().Select(Config.GetSelectorModeDisplayName));
        _shellOptions = new(Enum.GetValues<ShellType>().Select(Config.GetShellDisplayName));
        _scriptPlacementOptions = new(Enum.GetValues<ScriptPlacementBehavior>().Select(Config.GetScriptPlacementDisplayName));

        _isSuppressingChanges = true;
        SynchronizeFromConfig();
        _isSuppressingChanges = false;
    }

    #region 选项列表属性

    /// <summary>获取主题风格 ComboBox 的选项文本列表。</summary>
    /// <value>只读集合引用；元素数量与 <see cref="ThemeStyle"/> 枚举值数量相同；不为 null；非延迟计算。</value>
    public ObservableCollection<string> ThemeOptions => _themeOptions;

    /// <summary>获取显示语言 ComboBox 的选项文本列表。</summary>
    /// <value>只读集合引用；元素数量与 <see cref="DisplayLanguage"/> 枚举值数量相同；不为 null；非延迟计算。</value>
    public ObservableCollection<string> LanguageOptions => _languageOptions;

    /// <summary>获取编辑器性能策略 ComboBox 的选项文本列表。</summary>
    /// <value>只读集合引用；元素数量与 <see cref="EditorPerformance"/> 枚举值数量相同；不为 null；非延迟计算。</value>
    public ObservableCollection<string> PerformanceOptions => _performanceOptions;

    /// <summary>获取语言选择框模式 ComboBox 的选项文本列表。</summary>
    /// <value>只读集合引用；元素数量与 <see cref="LanguageSelectorMode"/> 枚举值数量相同；不为 null；非延迟计算。</value>
    public ObservableCollection<string> SelectorModeOptions => _selectorModeOptions;

    /// <summary>获取命令解释器类型 ComboBox 的选项文本列表。</summary>
    /// <value>只读集合引用；元素数量与 <see cref="ShellType"/> 枚举值数量相同；不为 null；非延迟计算。</value>
    public ObservableCollection<string> ShellOptions => _shellOptions;

    /// <summary>获取脚本放置行为 ComboBox 的选项文本列表。</summary>
    /// <value>只读集合引用；元素数量与 <see cref="ScriptPlacementBehavior"/> 枚举值数量相同；不为 null；非延迟计算。</value>
    public ObservableCollection<string> ScriptPlacementOptions => _scriptPlacementOptions;

    #endregion

    #region 选中索引属性

    /// <summary>获取或设置主题风格 ComboBox 的当前选中索引。</summary>
    /// <value>非负整数，对应 <see cref="ThemeStyle"/> 枚举的整数值；默认值由 Config 初始化；赋值小于 0 时不写入 Config；setter 同步写入 Config 并触发 <see cref="ThemeChanged"/>；需在 UI 线程访问。</value>
    public int SelectedThemeIndex
    {
        get => _selectedThemeIndex;
        set
        {
            if (SetProperty(ref _selectedThemeIndex, value) && !_isSuppressingChanges && value >= 0)
            {
                ThemeStyle theme = (ThemeStyle)value;
                Config.Theme = theme;
                ThemeChanged?.Invoke(theme);
            }
        }
    }

    /// <summary>获取或设置显示语言 ComboBox 的当前选中索引。</summary>
    /// <value>非负整数，对应 <see cref="DisplayLanguage"/> 枚举的整数值；默认值由 Config 初始化；赋值小于 0 时不写入 Config；setter 同步写入 Config 并触发 <see cref="LanguageChanged"/>；需在 UI 线程访问。</value>
    public int SelectedLanguageIndex
    {
        get => _selectedLanguageIndex;
        set
        {
            if (SetProperty(ref _selectedLanguageIndex, value) && !_isSuppressingChanges && value >= 0)
            {
                Config.Language = (DisplayLanguage)value;
                LanguageChanged?.Invoke();
            }
        }
    }

    /// <summary>获取或设置编辑器性能策略 ComboBox 的当前选中索引。</summary>
    /// <value>非负整数，对应 <see cref="EditorPerformance"/> 枚举的整数值；默认值由 Config 初始化；赋值小于 0 时不触发事件；不直接写入 Config，而是触发 <see cref="PerformanceChangeRequested"/> 由 View 层确认后写入；需在 UI 线程访问。</value>
    public int SelectedPerformanceIndex
    {
        get => _selectedPerformanceIndex;
        set
        {
            int previousValue = _selectedPerformanceIndex;
            if (SetProperty(ref _selectedPerformanceIndex, value) && !_isSuppressingChanges && value >= 0)
            {
                PerformanceChangeRequested?.Invoke(previousValue, value);
            }
        }
    }

    /// <summary>获取或设置语言选择框模式 ComboBox 的当前选中索引。</summary>
    /// <value>非负整数，对应 <see cref="LanguageSelectorMode"/> 枚举的整数值；默认值由 Config 初始化；赋值小于 0 时不写入 Config；setter 同步写入 Config；需在 UI 线程访问。</value>
    public int SelectedSelectorModeIndex
    {
        get => _selectedSelectorModeIndex;
        set
        {
            if (SetProperty(ref _selectedSelectorModeIndex, value) && !_isSuppressingChanges && value >= 0)
            {
                Config.SelectorMode = (LanguageSelectorMode)value;
            }
        }
    }

    /// <summary>获取或设置命令解释器类型 ComboBox 的当前选中索引。</summary>
    /// <value>非负整数，对应 <see cref="ShellType"/> 枚举的整数值；默认值由 Config 初始化；赋值小于 0 时不写入 Config；setter 同步写入 Config；需在 UI 线程访问。</value>
    public int SelectedShellIndex
    {
        get => _selectedShellIndex;
        set
        {
            if (SetProperty(ref _selectedShellIndex, value) && !_isSuppressingChanges && value >= 0)
            {
                Config.Shell = (ShellType)value;
            }
        }
    }

    /// <summary>获取或设置脚本放置行为 ComboBox 的当前选中索引。</summary>
    /// <value>非负整数，对应 <see cref="ScriptPlacementBehavior"/> 枚举的整数值；默认值由 Config 初始化；赋值小于 0 时不触发事件；不直接写入 Config，而是触发 <see cref="ScriptPlacementChangeRequested"/> 由 View 层确认后写入；需在 UI 线程访问。</value>
    public int SelectedScriptPlacementIndex
    {
        get => _selectedScriptPlacementIndex;
        set
        {
            int previousValue = _selectedScriptPlacementIndex;
            if (SetProperty(ref _selectedScriptPlacementIndex, value) && !_isSuppressingChanges && value >= 0)
            {
                ScriptPlacementChangeRequested?.Invoke(previousValue, value);
            }
        }
    }

    #endregion

    #region 性能策略确认/撤销

    /// <summary>
    /// 确认编辑器性能策略变更，将新索引对应的值写入 Config 持久化。
    /// </summary>
    /// <param name="newIndex">已确认的新选项索引；必须为非负整数且在 <see cref="EditorPerformance"/> 枚举范围内；调用方负责传入合法值。</param>
    public void ConfirmPerformanceChange(int newIndex)
    {
        Config.Performance = (EditorPerformance)newIndex;
    }

    /// <summary>
    /// 撤销编辑器性能策略变更，将 ComboBox 恢复到变更前的选项索引。
    /// </summary>
    /// <param name="oldIndex">变更前的选项索引；必须为非负整数且在 <see cref="EditorPerformance"/> 枚举范围内；调用方负责传入合法值。</param>
    public void RevertPerformanceChange(int oldIndex)
    {
        _isSuppressingChanges = true;
        SelectedPerformanceIndex = oldIndex;
        _isSuppressingChanges = false;
    }

    #endregion

    #region 脚本放置行为确认/撤销

    /// <summary>
    /// 确认脚本放置行为变更，将新索引对应的值写入 Config 持久化。
    /// </summary>
    /// <param name="newIndex">已确认的新选项索引；必须为非负整数且在 <see cref="ScriptPlacementBehavior"/> 枚举范围内；调用方负责传入合法值。</param>
    public void ConfirmScriptPlacement(int newIndex)
    {
        Config.ScriptPlacement = (ScriptPlacementBehavior)newIndex;
    }

    /// <summary>
    /// 撤销脚本放置行为变更，将 ComboBox 恢复到变更前的选项索引。
    /// </summary>
    /// <param name="oldIndex">变更前的选项索引；必须为非负整数且在 <see cref="ScriptPlacementBehavior"/> 枚举范围内；调用方负责传入合法值。</param>
    public void RevertScriptPlacement(int oldIndex)
    {
        _isSuppressingChanges = true;
        SelectedScriptPlacementIndex = oldIndex;
        _isSuppressingChanges = false;
    }

    #endregion

    #region 开关属性

    /// <summary>获取或设置执行前是否显示确认对话框。</summary>
    /// <value>布尔值；默认值由 Config 初始化；setter 同步写入 Config；需在 UI 线程访问。</value>
    public bool ConfirmBeforeExecution
    {
        get => _confirmBeforeExecution;
        set
        {
            if (SetProperty(ref _confirmBeforeExecution, value) && !_isSuppressingChanges)
            {
                Config.ConfirmBeforeExecution = value;
            }
        }
    }

    /// <summary>获取或设置开始执行代码时是否自动退出应用程序。</summary>
    /// <value>布尔值；默认值由 Config 初始化；setter 同步写入 Config；需在 UI 线程访问。</value>
    public bool AutoExitOnExecution
    {
        get => _autoExitOnExecution;
        set
        {
            if (SetProperty(ref _autoExitOnExecution, value) && !_isSuppressingChanges)
            {
                Config.AutoExitOnExecution = value;
            }
        }
    }

    /// <summary>获取或设置代码运行完成后是否自动关闭终端窗口。</summary>
    /// <value>布尔值；默认值由 Config 初始化；setter 同步写入 Config；需在 UI 线程访问。</value>
    public bool AutoCloseTerminalOnCompletion
    {
        get => _autoCloseTerminalOnCompletion;
        set
        {
            if (SetProperty(ref _autoCloseTerminalOnCompletion, value) && !_isSuppressingChanges)
            {
                Config.AutoCloseTerminalOnCompletion = value;
            }
        }
    }

    #endregion

    #region 关于信息属性（只读）

    /// <summary>获取应用程序名称。</summary>
    /// <value>来自 <see cref="Config.AppName"/> 的字符串；不为 null；非延迟计算。</value>
    public string AppName => Config.AppName;

    /// <summary>获取应用程序版本号字符串。</summary>
    /// <value>语义化版本格式字符串；来自 <see cref="Config.Version"/>；不为 null；非延迟计算。</value>
    public string Version => Config.Version;

    /// <summary>获取带 "v" 前缀的版本号显示文本。</summary>
    /// <value>格式为 "v{Version}" 的字符串；不为 null；每次访问时重新计算；用于界面展示。</value>
    public string VersionDisplay => $"v{Config.Version}";

    /// <summary>获取应用程序作者名称。</summary>
    /// <value>来自 <see cref="Config.Author"/> 的字符串；不为 null；非延迟计算。</value>
    public string Author => Config.Author;

    /// <summary>获取应用程序编译时间的格式化文本。</summary>
    /// <value>格式为 "yyyy-MM-dd HH:mm:ss"（InvariantCulture）的字符串；来源为程序集版本信息或文件写入时间；不为 null；每次访问时重新计算。</value>
    public string BuildTimeText => _buildTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>获取项目 GitHub 主页地址。</summary>
    /// <value>来自 <see cref="Config.GitHubUrl"/> 的字符串；不为 null；非延迟计算。</value>
    public string GitHubUrl => Config.GitHubUrl;

    /// <summary>获取是否存在 Microsoft Store 链接。</summary>
    /// <value>当 <see cref="Config.MicrosoftStoreUrl"/> 非空时为 true，否则为 false；每次访问时重新计算。</value>
    public bool HasStoreUrl => !string.IsNullOrEmpty(Config.MicrosoftStoreUrl);

    /// <summary>获取 Microsoft Store 产品页地址。</summary>
    /// <value>来自 <see cref="Config.MicrosoftStoreUrl"/> 的字符串；可能为空字符串；访问前应通过 <see cref="HasStoreUrl"/> 判断有效性；非延迟计算。</value>
    public string StoreUrl => Config.MicrosoftStoreUrl;

    #endregion

    #region 高级设置访问方法

    /// <summary>
    /// 获取当前临时文件名前缀配置值。
    /// </summary>
    /// <returns>临时文件名前缀字符串；不为 null；直接透传自 <see cref="Config.TempFilePrefix"/>。</returns>
    public string GetTempFilePrefix() => Config.TempFilePrefix;

    /// <summary>
    /// 获取当前语言自动识别置信度阈值配置值。
    /// </summary>
    /// <returns>置信度阈值浮点数；范围 [0.0, 1.0]；直接透传自 <see cref="Config.ConfidenceThreshold"/>。</returns>
    public double GetConfidenceThreshold() => Config.ConfidenceThreshold;

    /// <summary>
    /// 获取所有语言的执行命令映射字典。
    /// </summary>
    /// <returns>以语言标识为键、执行命令模板为值的字典；不为 null；来自 <see cref="Config.GetAllLanguageCommands"/>。</returns>
    public Dictionary<string, string> GetAllLanguageCommands() => Config.GetAllLanguageCommands();

    /// <summary>
    /// 将高级设置的修改批量写入 Config 持久化，跳过无效或空白值。
    /// </summary>
    /// <param name="prefix">临时文件名前缀；不允许为 null；为纯空白字符串时跳过写入。</param>
    /// <param name="threshold">置信度阈值；范围 [0.0, 1.0]；为 NaN 或超出范围时跳过写入。</param>
    /// <param name="commands">语言执行命令映射字典；不允许为 null；命令值为空白字符串的条目将被跳过。</param>
    /// <exception cref="ArgumentNullException"><paramref name="prefix"/> 或 <paramref name="commands"/> 为 null 时抛出。</exception>
    public void SaveAdvancedSettings(string prefix, double threshold, Dictionary<string, string> commands)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(commands);

        if (!string.IsNullOrWhiteSpace(prefix))
        {
            Config.TempFilePrefix = prefix;
        }

        if (!double.IsNaN(threshold) && threshold is >= 0.0 and <= 1.0)
        {
            Config.ConfidenceThreshold = threshold;
        }

        foreach ((string language, string command) in commands)
        {
            if (!string.IsNullOrWhiteSpace(command))
            {
                Config.SetLanguageCommand(language, command);
            }
        }
    }

    /// <summary>
    /// 将高级设置全部重置为默认值，并返回重置后的当前配置值。
    /// </summary>
    /// <returns>包含重置后的临时文件前缀、置信度阈值及完整语言命令字典的元组；各字段均不为 null。</returns>
    public (string Prefix, double Threshold, Dictionary<string, string> Commands) ResetAdvancedToDefaults()
    {
        Config.TempFilePrefix = Config.DefaultTempFilePrefix;
        Config.ConfidenceThreshold = Config.DefaultConfidenceThreshold;
        Config.ResetAllLanguageCommands();

        return (Config.DefaultTempFilePrefix, Config.DefaultConfidenceThreshold, Config.GetAllLanguageCommands());
    }

    #endregion

    #region 整体重置

    /// <summary>
    /// 将所有设置重置为出厂默认值，并同步刷新 ViewModel 状态与选项文本。
    /// </summary>
    public void ResetAllSettings()
    {
        Config.ResetAllSettings();

        _isSuppressingChanges = true;
        RefreshOptionTexts();
        SynchronizeFromConfig();
        OnPropertyChanged(nameof(AppName));
        _isSuppressingChanges = false;
    }

    #endregion

    #region 语言切换刷新

    /// <summary>
    /// 在显示语言变更后刷新所有本地化相关的选项文本与 ViewModel 属性通知。
    /// </summary>
    public void RefreshAfterLanguageChange()
    {
        _isSuppressingChanges = true;
        RefreshOptionTexts();
        SynchronizeFromConfig();
        _isSuppressingChanges = false;
        OnPropertyChanged(nameof(AppName));
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

    #region 私有辅助方法

    /// <summary>
    /// 原地更新所有 ComboBox 选项列表的显示文本，保留集合对象引用以避免布局抖动。
    /// </summary>
    private void RefreshOptionTexts()
    {
        UpdateCollectionItems(_themeOptions, Enum.GetValues<ThemeStyle>().Select(Config.GetThemeDisplayName));
        UpdateCollectionItems(_languageOptions, Enum.GetValues<DisplayLanguage>().Select(Config.GetLanguageDisplayName));
        UpdateCollectionItems(_performanceOptions, Enum.GetValues<EditorPerformance>().Select(Config.GetPerformanceDisplayName));
        UpdateCollectionItems(_selectorModeOptions, Enum.GetValues<LanguageSelectorMode>().Select(Config.GetSelectorModeDisplayName));
        UpdateCollectionItems(_shellOptions, Enum.GetValues<ShellType>().Select(Config.GetShellDisplayName));
        UpdateCollectionItems(_scriptPlacementOptions, Enum.GetValues<ScriptPlacementBehavior>().Select(Config.GetScriptPlacementDisplayName));
    }

    /// <summary>
    /// 将 <paramref name="newItems"/> 中的元素逐位置写入 <paramref name="collection"/>，仅在文本不同时赋值以减少不必要的变更通知。
    /// </summary>
    /// <param name="collection">目标集合；长度必须与 <paramref name="newItems"/> 元素数量一致；不允许为 null。</param>
    /// <param name="newItems">新的显示文本序列；不允许为 null。</param>
    private static void UpdateCollectionItems(ObservableCollection<string> collection, IEnumerable<string> newItems)
    {
        int index = 0;
        foreach (string item in newItems)
        {
            if (index < collection.Count)
            {
                if (!string.Equals(collection[index], item, StringComparison.Ordinal))
                {
                    collection[index] = item;
                }
            }

            index++;
        }
    }

    /// <summary>
    /// 从 <see cref="Config"/> 读取当前配置值并同步写入所有对应的 ViewModel 属性。
    /// </summary>
    private void SynchronizeFromConfig()
    {
        SelectedThemeIndex = (int)Config.Theme;
        SelectedLanguageIndex = (int)Config.Language;
        SelectedPerformanceIndex = (int)Config.Performance;
        SelectedSelectorModeIndex = (int)Config.SelectorMode;
        SelectedShellIndex = (int)Config.Shell;
        SelectedScriptPlacementIndex = (int)Config.ScriptPlacement;
        ConfirmBeforeExecution = Config.ConfirmBeforeExecution;
        AutoExitOnExecution = Config.AutoExitOnExecution;
        AutoCloseTerminalOnCompletion = Config.AutoCloseTerminalOnCompletion;
    }

    /// <summary>
    /// 尝试从程序集元数据或文件系统获取编译时间戳，均失败时回退为当前时间。
    /// </summary>
    /// <returns>编译时间戳；两种来源均不可用时返回 <see cref="DateTime.Now"/>。</returns>
    private static DateTime RetrieveBuildTime()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();

        return TryGetBuildTimeFromVersion(assembly)
               ?? TryGetBuildTimeFromFile(assembly)
               ?? DateTime.Now;
    }

    /// <summary>
    /// 尝试从程序集的 <see cref="AssemblyInformationalVersionAttribute"/> 中解析编译时间戳。
    /// </summary>
    /// <param name="assembly">目标程序集；不允许为 null。</param>
    /// <returns>解析成功时返回编译时间；格式不符或属性缺失时返回 null。</returns>
    private static DateTime? TryGetBuildTimeFromVersion(Assembly assembly)
    {
        string? version = assembly
            .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
            .OfType<AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()
            ?.InformationalVersion;

        if (version is null)
        {
            return null;
        }

        int plusIndex = version.IndexOf('+');
        if (plusIndex < 0 || version.Length <= plusIndex + 14)
        {
            return null;
        }

        string timestampPart = version[(plusIndex + 1)..(plusIndex + 15)];
        return DateTime.TryParseExact(timestampPart, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedTime)
            ? parsedTime
            : null;
    }

    /// <summary>
    /// 尝试从程序集文件的最后写入时间获取编译时间戳。
    /// </summary>
    /// <param name="assembly">目标程序集；不允许为 null。</param>
    /// <returns>文件存在时返回最后写入时间；文件路径为空或文件不存在时返回 null。</returns>
    private static DateTime? TryGetBuildTimeFromFile(Assembly assembly)
    {
        string? filePath = assembly.Location;
        return !string.IsNullOrEmpty(filePath) && File.Exists(filePath)
            ? File.GetLastWriteTime(filePath)
            : null;
    }

    #endregion
}