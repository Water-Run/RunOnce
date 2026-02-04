/*
 * 脚本语言检测器
 * 通过代码特征分析自动识别脚本语言类型，输出按置信度排序的检测结果
 * 
 * @author: WaterRun
 * @file: Static/Detector.cs
 * @date: 2026-02-04
 */

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RunOnce.Static;

/// <summary>
/// 语言检测结果记录，承载单个语言的检测置信度信息。
/// </summary>
/// <remarks>
/// 不变量：Language 必须是 <see cref="Config.SupportedLanguages"/> 中的有效值；Confidence 范围为 [0.0, 1.0]。
/// 线程安全：作为不可变记录类型，天然线程安全。
/// 副作用：无。
/// </remarks>
/// <param name="Language">语言标识符，与 <see cref="Config.SupportedLanguages"/> 中的定义一致。</param>
/// <param name="Confidence">检测置信度，范围 [0.0, 1.0]，值越高表示越可能是该语言。</param>
public readonly record struct DetectionResult(string Language, double Confidence)
{
    /// <summary>
    /// 判断当前结果是否达到高置信度标准。
    /// </summary>
    /// <param name="threshold">置信度判定范围配置。</param>
    /// <returns>若置信度高于阈值上界则返回 true，否则返回 false。</returns>
    public bool IsHighConfidence(ConfidenceRange threshold) => threshold.IsHighConfidence(Confidence);

    /// <summary>
    /// 判断当前结果是否低于可信标准。
    /// </summary>
    /// <param name="threshold">置信度判定范围配置。</param>
    /// <returns>若置信度低于阈值下界则返回 true，否则返回 false。</returns>
    public bool IsLowConfidence(ConfidenceRange threshold) => threshold.IsLowConfidence(Confidence);
}

/// <summary>
/// 脚本语言检测器静态类，通过代码特征分析自动识别脚本语言类型。
/// </summary>
/// <remarks>
/// 不变量：所有检测规则为硬编码且不可变；检测结果始终覆盖 <see cref="Config.SupportedLanguages"/> 中的全部语言。
/// 线程安全：所有公开方法为线程安全，内部状态均为只读。
/// 副作用：无。
/// </remarks>
public static class Detector
{
    /// <summary>
    /// 确定性标记命中时的置信度分数。
    /// </summary>
    private const double DefinitiveScore = 0.98;

    /// <summary>
    /// 强特征单项基础分数。
    /// </summary>
    private const double StrongFeatureBaseScore = 0.25;

    /// <summary>
    /// 强特征组合最高分数上限。
    /// </summary>
    private const double StrongFeatureMaxScore = 0.92;

    /// <summary>
    /// 弱特征单项分数。
    /// </summary>
    private const double WeakFeatureScore = 0.08;

    /// <summary>
    /// 弱特征最高分数上限。
    /// </summary>
    private const double WeakFeatureMaxScore = 0.35;

    /// <summary>
    /// Shebang 行匹配正则表达式，预编译以提升性能。
    /// </summary>
    private static readonly Regex _shebangRegex = new(
        @"^#!\s*/(?:usr/(?:local/)?)?bin/(?:env\s+)?(\w+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Shebang 解释器名称到语言标识符的映射字典。
    /// </summary>
    private static readonly Dictionary<string, string> _shebangMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["python"] = "python",
        ["python3"] = "python",
        ["python2"] = "python",
        ["lua"] = "lua",
        ["pwsh"] = "powershell",
        ["bash"] = "bat",
        ["sh"] = "bat",
        ["nim"] = "nim",
    };

    /// <summary>
    /// 各语言的确定性标记检测规则集合，命中任一规则即可确定语言。
    /// </summary>
    private static readonly Dictionary<string, Func<string, bool>> _definitiveMarkers = new()
    {
        ["bat"] = code => code.StartsWith("@echo off", StringComparison.OrdinalIgnoreCase)
                          || code.StartsWith("@ECHO OFF", StringComparison.Ordinal)
                          || code.StartsWith("REM ", StringComparison.OrdinalIgnoreCase),
        ["go"] = code => Regex.IsMatch(code, @"^package\s+main\s*$", RegexOptions.Multiline),
        ["nim"] = code => code.Contains("proc ", StringComparison.Ordinal)
                          && code.Contains("=", StringComparison.Ordinal)
                          && (code.Contains("echo ", StringComparison.Ordinal) || code.Contains("import ", StringComparison.Ordinal)),
    };

    /// <summary>
    /// 各语言的强特征正则表达式集合，每个语言对应多个特征模式。
    /// </summary>
    private static readonly Dictionary<string, Regex[]> _strongFeatures = new()
    {
        ["bat"] =
        [
            new Regex(@"^set\s+\w+=", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase),
            new Regex(@"^if\s+(not\s+)?(exist|defined|errorlevel)", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase),
            new Regex(@"^for\s+%%\w+\s+in\s+\(", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase),
            new Regex(@"^goto\s+:\w+", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase),
            new Regex(@"^:\w+", RegexOptions.Compiled | RegexOptions.Multiline),
            new Regex(@"%\w+%", RegexOptions.Compiled),
        ],
        ["powershell"] =
        [
            new Regex(@"\$\w+\s*=", RegexOptions.Compiled),
            new Regex(@"\b(Get|Set|New|Remove|Add|Import|Export)-\w+", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"\bfunction\s+\w+", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"\bparam\s*\(", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"\[Parameter\s*\(", RegexOptions.Compiled),
            new Regex(@"\|\s*(Where-Object|ForEach-Object|Select-Object)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        ],
        ["python"] =
        [
            new Regex(@"\bdef\s+\w+\s*\([^)]*\)\s*:", RegexOptions.Compiled),
            new Regex(@"\bclass\s+\w+\s*[:\(]", RegexOptions.Compiled),
            new Regex(@"^import\s+\w+", RegexOptions.Compiled | RegexOptions.Multiline),
            new Regex(@"^from\s+\w+\s+import\s+", RegexOptions.Compiled | RegexOptions.Multiline),
            new Regex(@"if\s+__name__\s*==\s*['""]__main__['""]", RegexOptions.Compiled),
            new Regex(@"^\s{4}\S", RegexOptions.Compiled | RegexOptions.Multiline),
        ],
        ["lua"] =
        [
            new Regex(@"\blocal\s+\w+\s*=", RegexOptions.Compiled),
            new Regex(@"\bfunction\s+\w+\s*\([^)]*\)", RegexOptions.Compiled),
            new Regex(@"\bend\b", RegexOptions.Compiled),
            new Regex(@"\bthen\b", RegexOptions.Compiled),
            new Regex(@"\brequire\s*\(['""]", RegexOptions.Compiled),
            new Regex(@"\.\.(?!\.)", RegexOptions.Compiled),
        ],
        ["nim"] =
        [
            new Regex(@"\bproc\s+\w+\s*\([^)]*\)", RegexOptions.Compiled),
            new Regex(@"\bvar\s+\w+\s*:", RegexOptions.Compiled),
            new Regex(@"\blet\s+\w+\s*=", RegexOptions.Compiled),
            new Regex(@"\becho\s+", RegexOptions.Compiled),
            new Regex(@"\bimport\s+\w+", RegexOptions.Compiled),
            new Regex(@"\bresult\s*=", RegexOptions.Compiled),
        ],
        ["go"] =
        [
            new Regex(@"\bfunc\s+\w+\s*\([^)]*\)", RegexOptions.Compiled),
            new Regex(@"\bpackage\s+\w+", RegexOptions.Compiled),
            new Regex(@":=", RegexOptions.Compiled),
            new Regex(@"\bimport\s+\(", RegexOptions.Compiled),
            new Regex(@"\bfmt\.(Print|Println|Printf|Sprintf)", RegexOptions.Compiled),
            new Regex(@"\bdefer\s+", RegexOptions.Compiled),
        ],
    };

    /// <summary>
    /// 各语言的弱特征关键字集合。
    /// </summary>
    private static readonly Dictionary<string, string[]> _weakFeatures = new()
    {
        ["bat"] = ["echo", "pause", "exit", "call", "start", "copy", "move", "del", "mkdir", "rmdir"],
        ["powershell"] = ["Write-Host", "Write-Output", "-eq", "-ne", "-gt", "-lt", "$true", "$false", "$null"],
        ["python"] = ["print", "elif", "except", "finally", "lambda", "yield", "with", "assert", "pass", "raise"],
        ["lua"] = ["nil", "elseif", "repeat", "until", "pairs", "ipairs", "table", "string", "math"],
        ["nim"] = ["nil", "true", "false", "and", "or", "not", "div", "mod", "shl", "shr"],
        ["go"] = ["nil", "make", "append", "len", "cap", "range", "chan", "select", "go", "struct"],
    };

    /// <summary>
    /// 对代码进行语言检测，返回按置信度降序排列的检测结果列表。
    /// </summary>
    /// <param name="code">待检测的代码字符串，允许为 null 或空字符串。</param>
    /// <returns>
    /// 包含所有支持语言及其置信度的检测结果列表，按置信度从高到低排序。
    /// 若输入为 null 或空白字符串，返回所有语言置信度为 0 的结果列表。
    /// </returns>
    /// <remarks>
    /// 检测分三层执行：确定性标记（命中即返回高分）、强特征组合（累积计分）、弱特征兜底（辅助参考）。
    /// </remarks>
    public static IReadOnlyList<DetectionResult> Detect(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Config.SupportedLanguages
                .Select(lang => new DetectionResult(lang, 0.0))
                .ToList();
        }

        string? definitiveLanguage = CheckDefinitiveMarkers(code);
        if (definitiveLanguage is not null)
        {
            return BuildDefinitiveResults(definitiveLanguage);
        }

        Dictionary<string, double> scores = CalculateFeatureScores(code);

        return Config.SupportedLanguages
            .Select(lang => new DetectionResult(lang, scores.GetValueOrDefault(lang, 0.0)))
            .OrderByDescending(r => r.Confidence)
            .ThenBy(r => r.Language)
            .ToList();
    }

    /// <summary>
    /// 获取代码最可能的语言检测结果。
    /// </summary>
    /// <param name="code">待检测的代码字符串，允许为 null 或空字符串。</param>
    /// <returns>
    /// 置信度最高的检测结果；若输入为 null 或空白字符串，返回首个支持语言且置信度为 0 的结果。
    /// </returns>
    public static DetectionResult DetectTop(string? code)
    {
        return Detect(code).First();
    }

    /// <summary>
    /// 获取代码的前 N 个最可能的语言检测结果。
    /// </summary>
    /// <param name="code">待检测的代码字符串，允许为 null 或空字符串。</param>
    /// <param name="count">返回结果的数量，必须大于 0。</param>
    /// <returns>置信度最高的前 N 个检测结果列表，若 count 大于支持的语言数量则返回全部结果。</returns>
    /// <exception cref="ArgumentOutOfRangeException">当 count 小于等于 0 时抛出。</exception>
    public static IReadOnlyList<DetectionResult> DetectTopN(string? code, int count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, Text.Localize("结果数量必须大于 0。"));
        }

        return Detect(code).Take(count).ToList();
    }

    /// <summary>
    /// 检查代码是否包含确定性标记。
    /// </summary>
    /// <param name="code">待检查的代码字符串，已验证非空。</param>
    /// <returns>若命中确定性标记则返回对应的语言标识符，否则返回 null。</returns>
    private static string? CheckDefinitiveMarkers(string code)
    {
        string? shebangLanguage = CheckShebang(code);
        if (shebangLanguage is not null)
        {
            return shebangLanguage;
        }

        foreach (KeyValuePair<string, Func<string, bool>> marker in _definitiveMarkers)
        {
            if (marker.Value(code))
            {
                return marker.Key;
            }
        }

        return null;
    }

    /// <summary>
    /// 检查代码首行是否包含 Shebang 声明。
    /// </summary>
    /// <param name="code">待检查的代码字符串，已验证非空。</param>
    /// <returns>若首行包含有效的 Shebang 声明则返回对应的语言标识符，否则返回 null。</returns>
    private static string? CheckShebang(string code)
    {
        int firstLineEnd = code.IndexOf('\n');
        string firstLine = firstLineEnd >= 0 ? code[..firstLineEnd] : code;

        Match match = _shebangRegex.Match(firstLine);
        if (!match.Success)
        {
            return null;
        }

        string interpreter = match.Groups[1].Value;
        return _shebangMappings.GetValueOrDefault(interpreter);
    }

    /// <summary>
    /// 构建确定性标记命中时的检测结果列表。
    /// </summary>
    /// <param name="language">命中的语言标识符。</param>
    /// <returns>命中语言置信度为 0.98，其余语言置信度为 0 的结果列表。</returns>
    private static IReadOnlyList<DetectionResult> BuildDefinitiveResults(string language)
    {
        List<DetectionResult> results = Config.SupportedLanguages
            .Select(lang => new DetectionResult(
                lang,
                string.Equals(lang, language, StringComparison.OrdinalIgnoreCase) ? DefinitiveScore : 0.0))
            .OrderByDescending(r => r.Confidence)
            .ThenBy(r => r.Language)
            .ToList();

        return results;
    }

    /// <summary>
    /// 计算各语言的特征匹配分数。
    /// </summary>
    /// <param name="code">待分析的代码字符串，已验证非空。</param>
    /// <returns>语言标识符到置信度分数的映射字典。</returns>
    private static Dictionary<string, double> CalculateFeatureScores(string code)
    {
        Dictionary<string, double> scores = new(StringComparer.OrdinalIgnoreCase);

        foreach (string language in Config.SupportedLanguages)
        {
            double strongScore = CalculateStrongFeatureScore(code, language);
            double weakScore = CalculateWeakFeatureScore(code, language);
            double totalScore = Math.Min(strongScore + weakScore, 1.0);

            scores[language] = totalScore;
        }

        return scores;
    }

    /// <summary>
    /// 计算指定语言的强特征匹配分数。
    /// </summary>
    /// <param name="code">待分析的代码字符串。</param>
    /// <param name="language">目标语言标识符。</param>
    /// <returns>强特征匹配的累积分数，上限为 <see cref="StrongFeatureMaxScore"/>。</returns>
    private static double CalculateStrongFeatureScore(string code, string language)
    {
        if (!_strongFeatures.TryGetValue(language, out Regex[]? patterns))
        {
            return 0.0;
        }

        int matchCount = patterns.Count(pattern => pattern.IsMatch(code));

        double scaledScore = matchCount switch
        {
            >= 4 => StrongFeatureMaxScore,
            3 => StrongFeatureBaseScore * 3 + 0.1,
            2 => StrongFeatureBaseScore * 2 + 0.05,
            1 => StrongFeatureBaseScore,
            _ => 0.0
        };

        return Math.Min(scaledScore, StrongFeatureMaxScore);
    }

    /// <summary>
    /// 计算指定语言的弱特征匹配分数。
    /// </summary>
    /// <param name="code">待分析的代码字符串。</param>
    /// <param name="language">目标语言标识符。</param>
    /// <returns>弱特征匹配的累积分数，上限为 <see cref="WeakFeatureMaxScore"/>。</returns>
    private static double CalculateWeakFeatureScore(string code, string language)
    {
        if (!_weakFeatures.TryGetValue(language, out string[]? keywords))
        {
            return 0.0;
        }

        int matchCount = keywords.Count(keyword =>
            code.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        double rawScore = matchCount * WeakFeatureScore;
        return Math.Min(rawScore, WeakFeatureMaxScore);
    }
}