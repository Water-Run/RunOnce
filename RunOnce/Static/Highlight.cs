/*
 * 语法高亮分析器
 * 提供代码文本的语法高亮区间分析，支持多种脚本语言的关键字、字符串、注释、数字识别
 * 
 * @author: WaterRun
 * @file: Static/Highlight.cs
 * @date: 2026-02-04
 */

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RunOnce.Static;

/// <summary>
/// 高亮 Token 类型枚举，定义语法高亮支持的元素类别。
/// </summary>
public enum TokenType
{
    /// <summary>语言关键字，如 if、for、function 等。</summary>
    Keyword,

    /// <summary>字符串字面量，包括单引号和双引号字符串。</summary>
    String,

    /// <summary>注释，包括单行注释和多行注释。</summary>
    Comment,

    /// <summary>数字字面量，包括整数和浮点数。</summary>
    Number,
}

/// <summary>
/// 高亮区间记录，承载单个语法元素的位置与类型信息。
/// </summary>
/// <remarks>
/// 不变量：Start 必须非负；Length 必须为正数；Start + Length 不超过源代码长度。
/// 线程安全：作为不可变记录类型，天然线程安全。
/// 副作用：无。
/// </remarks>
/// <param name="Start">区间起始位置，基于字符索引，从 0 开始。</param>
/// <param name="Length">区间长度，必须大于 0。</param>
/// <param name="Type">Token 类型，决定该区间应使用的颜色类别。</param>
public readonly record struct HighlightSpan(int Start, int Length, TokenType Type)
{
    /// <summary>
    /// 获取区间的结束位置（不包含）。
    /// </summary>
    /// <value>Start + Length 的计算结果。</value>
    public int End => Start + Length;

    /// <summary>
    /// 判断当前区间是否与另一区间重叠。
    /// </summary>
    /// <param name="other">待比较的另一区间。</param>
    /// <returns>若两区间有交集则返回 true，否则返回 false。</returns>
    public bool Overlaps(HighlightSpan other) => Start < other.End && End > other.Start;
}

/// <summary>
/// 语法高亮分析器静态类，提供代码文本的语法高亮区间分析功能。
/// </summary>
/// <remarks>
/// 不变量：所有高亮规则为硬编码且不可变；返回的区间列表不包含重叠区间。
/// 线程安全：所有公开方法为线程安全，内部状态均为只读。
/// 副作用：无。
/// </remarks>
public static class Highlight
{
    /// <summary>
    /// 各语言的关键字集合，键为语言标识符，值为关键字数组。
    /// </summary>
    private static readonly Dictionary<string, string[]> _keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bat"] =
        [
            "call", "cd", "chdir", "cls", "cmd", "color", "copy", "del", "dir", "echo", "else",
            "endlocal", "equ", "errorlevel", "exist", "exit", "for", "geq", "goto", "gtr", "if",
            "in", "leq", "lss", "md", "mkdir", "move", "neq", "not", "nul", "path", "pause", "popd",
            "pushd", "rd", "rem", "ren", "rename", "rmdir", "set", "setlocal", "shift", "start",
            "title", "type", "ver", "verify", "vol"
        ],
        ["powershell"] =
        [
            "Begin", "Break", "Catch", "Class", "Continue", "Data", "Define", "Do", "DynamicParam",
            "Else", "ElseIf", "End", "Exit", "Filter", "Finally", "For", "ForEach", "From", "Function",
            "If", "In", "Param", "Process", "Return", "Switch", "Throw", "Trap", "Try", "Until",
            "Using", "While", "Workflow"
        ],
        ["python"] =
        [
            "False", "None", "True", "and", "as", "assert", "async", "await", "break", "class",
            "continue", "def", "del", "elif", "else", "except", "finally", "for", "from", "global",
            "if", "import", "in", "is", "lambda", "nonlocal", "not", "or", "pass", "raise",
            "return", "try", "while", "with", "yield"
        ],
        ["lua"] =
        [
            "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "goto",
            "if", "in", "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while"
        ],
        ["nim"] =
        [
            "addr", "and", "as", "asm", "bind", "block", "break", "case", "cast", "concept", "const",
            "continue", "converter", "defer", "discard", "distinct", "div", "do", "elif", "else",
            "end", "enum", "except", "export", "finally", "for", "from", "func", "if", "import", "in",
            "include", "interface", "is", "isnot", "iterator", "let", "macro", "method", "mixin",
            "mod", "nil", "not", "notin", "object", "of", "or", "out", "proc", "ptr", "raise", "ref",
            "return", "shl", "shr", "static", "template", "try", "tuple", "type", "using", "var",
            "when", "while", "xor", "yield"
        ],
        ["go"] =
        [
            "break", "case", "chan", "const", "continue", "default", "defer", "else", "fallthrough",
            "for", "func", "go", "goto", "if", "import", "interface", "map", "package", "range",
            "return", "select", "struct", "switch", "type", "var"
        ],
    };

    /// <summary>
    /// 各语言的注释模式配置，包含单行注释前缀和多行注释分隔符。
    /// </summary>
    private static readonly Dictionary<string, CommentPattern> _commentPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bat"] = new CommentPattern(["REM ", "rem ", "::"], null, null),
        ["powershell"] = new CommentPattern(["#"], "<#", "#>"),
        ["python"] = new CommentPattern(["#"], null, null),
        ["lua"] = new CommentPattern(["--"], "--[[", "]]"),
        ["nim"] = new CommentPattern(["#"], "#[", "]#"),
        ["go"] = new CommentPattern(["//"], "/*", "*/"),
    };

    /// <summary>
    /// 各语言的字符串分隔符配置。
    /// </summary>
    private static readonly Dictionary<string, StringPattern> _stringPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bat"] = new StringPattern(['"'], false, null),
        ["powershell"] = new StringPattern(['"', '\''], false, null),
        ["python"] = new StringPattern(['"', '\''], true, ["\"\"\"", "'''"]),
        ["lua"] = new StringPattern(['"', '\''], false, ["[[", "]]"]),
        ["nim"] = new StringPattern(['"'], true, ["\"\"\""]),
        ["go"] = new StringPattern(['"', '\'', '`'], false, null),
    };

    /// <summary>
    /// 匹配数字字面量的正则表达式，支持整数、浮点数、十六进制、科学计数法。
    /// </summary>
    private static readonly Regex _numberRegex = new(
        @"\b(?:0[xX][0-9a-fA-F]+|0[bB][01]+|0[oO][0-7]+|\d+\.?\d*(?:[eE][+-]?\d+)?|\.\d+(?:[eE][+-]?\d+)?)\b",
        RegexOptions.Compiled);

    /// <summary>
    /// 对代码进行语法高亮分析，返回高亮区间列表。
    /// </summary>
    /// <param name="code">待分析的代码字符串，允许为 null 或空字符串。</param>
    /// <param name="language">脚本语言标识符，必须是支持的语言之一，允许为 null 或空字符串。</param>
    /// <returns>
    /// 按起始位置升序排列的高亮区间列表，区间之间不重叠。
    /// 若输入代码为空或语言不支持，返回空列表。
    /// </returns>
    public static IReadOnlyList<HighlightSpan> Analyze(string? code, string? language)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrWhiteSpace(language))
        {
            return [];
        }

        if (!_keywords.ContainsKey(language))
        {
            return [];
        }

        List<HighlightSpan> spans = [];
        HashSet<(int Start, int End)> occupiedRanges = [];

        AnalyzeComments(code, language, spans, occupiedRanges);
        AnalyzeStrings(code, language, spans, occupiedRanges);
        AnalyzeNumbers(code, spans, occupiedRanges);
        AnalyzeKeywords(code, language, spans, occupiedRanges);

        return spans.OrderBy(s => s.Start).ThenBy(s => s.Length).ToList();
    }

    /// <summary>
    /// 分析代码中的注释并添加到高亮区间列表。
    /// </summary>
    /// <param name="code">待分析的代码字符串。</param>
    /// <param name="language">脚本语言标识符。</param>
    /// <param name="spans">高亮区间列表，分析结果将追加到此列表。</param>
    /// <param name="occupiedRanges">已占用的区间集合，用于避免重叠。</param>
    private static void AnalyzeComments(string code, string language, List<HighlightSpan> spans, HashSet<(int Start, int End)> occupiedRanges)
    {
        if (!_commentPatterns.TryGetValue(language, out CommentPattern pattern))
        {
            return;
        }

        if (pattern.MultiLineStart is not null && pattern.MultiLineEnd is not null)
        {
            int searchStart = 0;
            while (searchStart < code.Length)
            {
                int startIndex = code.IndexOf(pattern.MultiLineStart, searchStart, StringComparison.Ordinal);
                if (startIndex < 0)
                {
                    break;
                }

                int endIndex = code.IndexOf(pattern.MultiLineEnd, startIndex + pattern.MultiLineStart.Length, StringComparison.Ordinal);
                int spanEnd = endIndex >= 0 ? endIndex + pattern.MultiLineEnd.Length : code.Length;

                if (!IsRangeOccupied(startIndex, spanEnd, occupiedRanges))
                {
                    spans.Add(new HighlightSpan(startIndex, spanEnd - startIndex, TokenType.Comment));
                    occupiedRanges.Add((startIndex, spanEnd));
                }

                searchStart = spanEnd;
            }
        }

        foreach (string prefix in pattern.SingleLinePrefixes)
        {
            int searchStart = 0;
            while (searchStart < code.Length)
            {
                int startIndex = code.IndexOf(prefix, searchStart, StringComparison.OrdinalIgnoreCase);
                if (startIndex < 0)
                {
                    break;
                }

                int lineEnd = code.IndexOf('\n', startIndex);
                int spanEnd = lineEnd >= 0 ? lineEnd : code.Length;

                if (!IsRangeOccupied(startIndex, spanEnd, occupiedRanges))
                {
                    spans.Add(new HighlightSpan(startIndex, spanEnd - startIndex, TokenType.Comment));
                    occupiedRanges.Add((startIndex, spanEnd));
                }

                searchStart = spanEnd + 1;
            }
        }
    }

    /// <summary>
    /// 分析代码中的字符串字面量并添加到高亮区间列表。
    /// </summary>
    /// <param name="code">待分析的代码字符串。</param>
    /// <param name="language">脚本语言标识符。</param>
    /// <param name="spans">高亮区间列表，分析结果将追加到此列表。</param>
    /// <param name="occupiedRanges">已占用的区间集合，用于避免重叠。</param>
    private static void AnalyzeStrings(string code, string language, List<HighlightSpan> spans, HashSet<(int Start, int End)> occupiedRanges)
    {
        if (!_stringPatterns.TryGetValue(language, out StringPattern pattern))
        {
            return;
        }

        if (pattern.MultiLineDelimiters is not null)
        {
            for (int i = 0; i < pattern.MultiLineDelimiters.Length; i += 2)
            {
                string startDelim = pattern.MultiLineDelimiters[i];
                string endDelim = i + 1 < pattern.MultiLineDelimiters.Length ? pattern.MultiLineDelimiters[i + 1] : startDelim;

                int searchStart = 0;
                while (searchStart < code.Length)
                {
                    int startIndex = code.IndexOf(startDelim, searchStart, StringComparison.Ordinal);
                    if (startIndex < 0)
                    {
                        break;
                    }

                    if (IsRangeOccupied(startIndex, startIndex + 1, occupiedRanges))
                    {
                        searchStart = startIndex + 1;
                        continue;
                    }

                    int endIndex = code.IndexOf(endDelim, startIndex + startDelim.Length, StringComparison.Ordinal);
                    int spanEnd = endIndex >= 0 ? endIndex + endDelim.Length : code.Length;

                    spans.Add(new HighlightSpan(startIndex, spanEnd - startIndex, TokenType.String));
                    occupiedRanges.Add((startIndex, spanEnd));

                    searchStart = spanEnd;
                }
            }
        }

        foreach (char delimiter in pattern.Delimiters)
        {
            int searchStart = 0;
            while (searchStart < code.Length)
            {
                int startIndex = code.IndexOf(delimiter, searchStart);
                if (startIndex < 0)
                {
                    break;
                }

                if (IsRangeOccupied(startIndex, startIndex + 1, occupiedRanges))
                {
                    searchStart = startIndex + 1;
                    continue;
                }

                int spanEnd = FindStringEnd(code, startIndex, delimiter, pattern.SupportsEscape);

                spans.Add(new HighlightSpan(startIndex, spanEnd - startIndex, TokenType.String));
                occupiedRanges.Add((startIndex, spanEnd));

                searchStart = spanEnd;
            }
        }
    }

    /// <summary>
    /// 查找字符串字面量的结束位置。
    /// </summary>
    /// <param name="code">代码字符串。</param>
    /// <param name="startIndex">字符串起始位置（包含开始分隔符）。</param>
    /// <param name="delimiter">字符串分隔符字符。</param>
    /// <param name="supportsEscape">是否支持反斜杠转义。</param>
    /// <returns>字符串结束位置（不包含），若未找到结束分隔符则返回代码末尾或行尾。</returns>
    private static int FindStringEnd(string code, int startIndex, char delimiter, bool supportsEscape)
    {
        int current = startIndex + 1;
        while (current < code.Length)
        {
            char c = code[current];

            if (c == '\n' && delimiter != '`')
            {
                return current;
            }

            if (supportsEscape && c == '\\' && current + 1 < code.Length)
            {
                current += 2;
                continue;
            }

            if (c == delimiter)
            {
                return current + 1;
            }

            current++;
        }

        return code.Length;
    }

    /// <summary>
    /// 分析代码中的数字字面量并添加到高亮区间列表。
    /// </summary>
    /// <param name="code">待分析的代码字符串。</param>
    /// <param name="spans">高亮区间列表，分析结果将追加到此列表。</param>
    /// <param name="occupiedRanges">已占用的区间集合，用于避免重叠。</param>
    private static void AnalyzeNumbers(string code, List<HighlightSpan> spans, HashSet<(int Start, int End)> occupiedRanges)
    {
        foreach (Match match in _numberRegex.Matches(code))
        {
            int start = match.Index;
            int end = start + match.Length;

            if (!IsRangeOccupied(start, end, occupiedRanges))
            {
                spans.Add(new HighlightSpan(start, match.Length, TokenType.Number));
                occupiedRanges.Add((start, end));
            }
        }
    }

    /// <summary>
    /// 分析代码中的关键字并添加到高亮区间列表。
    /// </summary>
    /// <param name="code">待分析的代码字符串。</param>
    /// <param name="language">脚本语言标识符。</param>
    /// <param name="spans">高亮区间列表，分析结果将追加到此列表。</param>
    /// <param name="occupiedRanges">已占用的区间集合，用于避免重叠。</param>
    private static void AnalyzeKeywords(string code, string language, List<HighlightSpan> spans, HashSet<(int Start, int End)> occupiedRanges)
    {
        if (!_keywords.TryGetValue(language, out string[]? keywords))
        {
            return;
        }

        bool isCaseSensitive = language is not ("bat" or "powershell");
        StringComparison comparison = isCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        foreach (string keyword in keywords)
        {
            int searchStart = 0;
            while (searchStart < code.Length)
            {
                int index = code.IndexOf(keyword, searchStart, comparison);
                if (index < 0)
                {
                    break;
                }

                bool isWordStart = index == 0 || !IsWordChar(code[index - 1]);
                bool isWordEnd = index + keyword.Length >= code.Length || !IsWordChar(code[index + keyword.Length]);

                if (isWordStart && isWordEnd && !IsRangeOccupied(index, index + keyword.Length, occupiedRanges))
                {
                    spans.Add(new HighlightSpan(index, keyword.Length, TokenType.Keyword));
                    occupiedRanges.Add((index, index + keyword.Length));
                }

                searchStart = index + 1;
            }
        }
    }

    /// <summary>
    /// 判断字符是否为单词组成字符（字母、数字或下划线）。
    /// </summary>
    /// <param name="c">待判断的字符。</param>
    /// <returns>若为单词字符则返回 true，否则返回 false。</returns>
    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// 判断指定区间是否与已占用区间重叠。
    /// </summary>
    /// <param name="start">区间起始位置。</param>
    /// <param name="end">区间结束位置（不包含）。</param>
    /// <param name="occupiedRanges">已占用的区间集合。</param>
    /// <returns>若存在重叠则返回 true，否则返回 false。</returns>
    private static bool IsRangeOccupied(int start, int end, HashSet<(int Start, int End)> occupiedRanges)
    {
        return occupiedRanges.Any(r => start < r.End && end > r.Start);
    }

    /// <summary>
    /// 注释模式配置记录，定义语言的注释语法规则。
    /// </summary>
    /// <param name="SingleLinePrefixes">单行注释前缀数组。</param>
    /// <param name="MultiLineStart">多行注释起始分隔符，若不支持多行注释则为 null。</param>
    /// <param name="MultiLineEnd">多行注释结束分隔符，若不支持多行注释则为 null。</param>
    private readonly record struct CommentPattern(string[] SingleLinePrefixes, string? MultiLineStart, string? MultiLineEnd);

    /// <summary>
    /// 字符串模式配置记录，定义语言的字符串字面量语法规则。
    /// </summary>
    /// <param name="Delimiters">字符串分隔符字符数组。</param>
    /// <param name="SupportsEscape">是否支持反斜杠转义序列。</param>
    /// <param name="MultiLineDelimiters">多行字符串分隔符数组（成对出现：起始、结束），若不支持则为 null。</param>
    private readonly record struct StringPattern(char[] Delimiters, bool SupportsEscape, string[]? MultiLineDelimiters);
}