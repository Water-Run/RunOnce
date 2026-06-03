/*
 * LLM API 客户端
 * 提供与 OpenAI 兼容的 LLM API 交互功能，包括连接验证、脚本生成与二次校对
 *
 * @author: WaterRun
 * @file: Static/LlmClient.cs
 * @date: 2026-03-27
 */

#nullable enable

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RunOnce.Static;

/// <summary>LLM API 客户端，封装连接验证、脚本生成与二次校对能力。</summary>
/// <remarks>
/// 不变量：<see cref="_httpClient"/> 全局唯一，复用底层 TCP 连接以避免套接字耗尽。
/// 线程安全：所有公共方法均为线程安全，每次调用无共享可变状态（<see cref="IsConnectionVerified"/> 除外，仅在 UI 线程读写）。
/// 副作用：发起 HTTP 请求并消耗 LLM API 额度。
/// </remarks>
public static class LlmClient
{
    /// <summary>连接验证请求使用的最大 Token 上限，避免低额度模型因默认生成上限过高拒绝请求。</summary>
    private const int VerifyMaxTokens = 1024;

    /// <summary>
    /// 全局共享的 <see cref="HttpClient"/> 实例。
    /// 超时设为无限，由调用方通过 <see cref="CancellationToken"/> 控制取消与超时。
    /// 生命周期与应用程序进程一致，不可释放。
    /// </summary>
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    /// <summary>
    /// 当前会话内 LLM 连接是否已通过验证；仅保存于内存，每次启动需重新验证。
    /// </summary>
    /// <value>true 表示本次会话已成功验证过连接，false 表示未验证或验证失败。</value>
    public static bool IsConnectionVerified { get; private set; }

    /// <summary>验证当前 LLM 配置的连接可达性，向 API 发送一次等价于实际生成的聊天请求。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>验证成功返回 true；API Key 为空时返回 false。</returns>
    /// <exception cref="InvalidOperationException">当 API 返回错误时抛出，包含具体错误信息。</exception>
    /// <exception cref="TimeoutException">当请求超时时抛出。</exception>
    /// <exception cref="HttpRequestException">当网络传输失败时抛出。</exception>
    /// <exception cref="OperationCanceledException">当 <paramref name="cancellationToken"/> 被触发时抛出。</exception>
    /// <remarks>
    /// 取消语义：支持通过 <paramref name="cancellationToken"/> 取消。
    /// 线程/重入：可安全并发调用。
    /// I/O：向 <see cref="Config.LlmBaseUrl"/> 发起一次 HTTP POST 请求，使用与 <see cref="GenerateScriptAsync"/> 相同的请求路径。
    /// </remarks>
    /// <summary>通过发起一次与实际生成完全一致的请求来检测当前 LLM 配置是否可用。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>检测成功返回 true；API Key 为空时返回 false。</returns>
    /// <exception cref="InvalidOperationException">当 API 返回错误时抛出，包含具体错误信息。</exception>
    /// <exception cref="TimeoutException">当请求超时时抛出。</exception>
    /// <exception cref="HttpRequestException">当网络传输失败时抛出。</exception>
    /// <exception cref="OperationCanceledException">当 <paramref name="cancellationToken"/> 被触发时抛出。</exception>
    /// <remarks>
    /// 取消语义：支持通过 <paramref name="cancellationToken"/> 取消。
    /// 线程/重入：可安全并发调用。
    /// I/O：向 <see cref="Config.LlmBaseUrl"/> 发起一次 HTTP POST 请求，
    /// 使用与 <see cref="GenerateScriptAsync"/> 相同的系统提示词、max_tokens 与超时参数，确保"能跑通验证即能跑通生成"。
    /// </remarks>
    public static async Task<bool> VerifyConnectionAsync(CancellationToken cancellationToken = default)
    {
        string apiKey = Config.LlmApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            IsConnectionVerified = false;
            return false;
        }

        try
        {
            string language = Config.LlmLanguagePreference;
            string systemContent = BuildSystemPrompt(language);

            await SendChatRequestAsync(
                    systemContent,
                    "Generate a script that prints hello world.",
                    Math.Min(Config.LlmMaxTokens, VerifyMaxTokens),
                    Config.LlmTimeoutSeconds,
                    cancellationToken)
                .ConfigureAwait(false);

            IsConnectionVerified = true;
            return true;
        }
        catch
        {
            IsConnectionVerified = false;
            throw;
        }
    }

    /// <summary>根据用户需求描述调用 LLM API 生成可执行脚本代码。</summary>
    /// <param name="userPrompt">用户输入的需求描述，不允许为 null 或空白字符串。</param>
    /// <param name="preferredLanguage">
    /// 用户选择的脚本语言标识符（如 "python"）；
    /// 为 null 时使用 <see cref="Config.LlmLanguagePreference"/>。
    /// </param>
    /// <param name="cancellationToken">取消令牌，用于中止正在进行的网络请求。</param>
    /// <returns>LLM 生成的纯脚本代码字符串，已去除可能附带的 Markdown 围栏标记。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="userPrompt"/> 为 null 时抛出。</exception>
    /// <exception cref="ArgumentException">当 <paramref name="userPrompt"/> 为空白字符串时抛出。</exception>
    /// <exception cref="InvalidOperationException">当 API Key 未配置或 API 返回错误时抛出。</exception>
    /// <exception cref="TimeoutException">当请求超时时抛出。</exception>
    /// <exception cref="HttpRequestException">当网络传输失败时抛出。</exception>
    /// <exception cref="OperationCanceledException">当取消令牌被触发时抛出。</exception>
    /// <remarks>
    /// 取消语义：支持通过 <paramref name="cancellationToken"/> 取消。
    /// 线程/重入：可安全并发调用。
    /// I/O：向 <see cref="Config.LlmBaseUrl"/> 发起一次 HTTP POST 请求。
    /// </remarks>
    public static async Task<string> GenerateScriptAsync(
        string userPrompt,
        string? preferredLanguage = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userPrompt);
        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            throw new ArgumentException(
                Text.Localize("需求描述不能为空。"), nameof(userPrompt));
        }

        string apiKey = Config.LlmApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                Text.Localize("尚未配置 API Key，请在设置中配置 LLM API Key。"));
        }

        string language = !string.IsNullOrWhiteSpace(preferredLanguage)
            ? preferredLanguage
            : Config.LlmLanguagePreference;

        string systemContent = BuildSystemPrompt(language);

        return await SendChatRequestAsync(systemContent, userPrompt, Config.LlmMaxTokens, Config.LlmTimeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>对已生成的代码进行二次校对，验证其是否符合用户需求。</summary>
    /// <param name="userPrompt">原始用户需求描述。不允许为 null 或空白。</param>
    /// <param name="generatedCode">待校对的生成代码。不允许为 null 或空白。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>
    /// 元组：Code 为最终代码（校对通过时为原始代码，未通过时为修正后的代码），
    /// Passed 表示原始代码是否被判定为正确。
    /// </returns>
    /// <exception cref="ArgumentNullException">当参数为 null 时抛出。</exception>
    /// <exception cref="InvalidOperationException">当 API 返回错误时抛出。</exception>
    /// <exception cref="TimeoutException">当请求超时时抛出。超时为配置值的两倍。</exception>
    /// <exception cref="OperationCanceledException">当取消令牌被触发时抛出。</exception>
    /// <remarks>
    /// 取消语义：支持取消。超时为 <see cref="Config.LlmTimeoutSeconds"/> 的两倍。
    /// 线程/重入：可安全并发调用。
    /// I/O：向 <see cref="Config.LlmBaseUrl"/> 发起一次 HTTP POST 请求。
    /// </remarks>
    public static async Task<(string Code, bool Passed)> DoubleCheckAsync(
        string userPrompt,
        string generatedCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userPrompt);
        ArgumentNullException.ThrowIfNull(generatedCode);

        string systemContent = Text.Localize(
            "你是一个代码审查助手。用户的需求是：") + userPrompt +
            Text.Localize("。请检查以下代码是否正确实现了该需求。如果代码正确，仅回复 PASS。如果代码有误，直接给出修正后的完整代码，不要包含任何解释或 Markdown 标记。");

        int doubleTimeout = Config.LlmTimeoutSeconds * 2;

        string response = await SendChatRequestAsync(
                systemContent, generatedCode, Config.LlmMaxTokens, doubleTimeout, cancellationToken)
            .ConfigureAwait(false);

        string trimmed = response.Trim();
        if (trimmed.Equals("PASS", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("PASS", StringComparison.OrdinalIgnoreCase))
        {
            return (generatedCode, true);
        }

        return (StripMarkdownCodeBlock(trimmed), false);
    }

    /// <summary>清除内存中的连接验证状态，使下次使用前需重新验证。</summary>
    public static void ResetVerificationState()
    {
        IsConnectionVerified = false;
    }

    #region 私有方法

    /// <summary>向 LLM API 发送一次聊天请求并返回助手回复内容。</summary>
    /// <param name="systemContent">系统提示词。</param>
    /// <param name="userContent">用户消息内容。</param>
    /// <param name="maxTokens">最大生成 Token 数。</param>
    /// <param name="timeoutSeconds">请求超时秒数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>提取并清理后的助手回复文本。</returns>
    private static async Task<string> SendChatRequestAsync(
        string systemContent,
        string userContent,
        int maxTokens,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        string apiKey = NormalizeBearerToken(Config.LlmApiKey);
        string baseUrl = Config.LlmBaseUrl.TrimEnd('/');

        var requestBody = new
        {
            model = Config.LlmModel,
            messages = new[]
            {
                new { role = "system", content = systemContent },
                new { role = "user", content = userContent },
            },
            max_tokens = maxTokens,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildChatCompletionsUrl(baseUrl));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient
                .SendAsync(request, timeoutCts.Token)
                .ConfigureAwait(false);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                Text.Localize("LLM API 请求超时，请检查网络连接或在设置中增加超时时间。"), ex);
        }

        string responseJson = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                Text.Localize(
                    "LLM API 返回错误 ({0}): {1}",
                    (int)response.StatusCode,
                    ExtractErrorMessage(responseJson)));
        }

        return ExtractGeneratedCode(responseJson);
    }

    /// <summary>根据目标语言与附加提示词构建系统提示词。</summary>
    /// <param name="language">目标脚本语言标识符。</param>
    /// <returns>完整的系统提示词。</returns>
    private static string BuildSystemPrompt(string language)
    {
        string basePrompt = Text.Localize(
            "你是一个专业的脚本生成助手。根据用户的需求，使用 {0} 语言生成可执行的脚本代码。仅输出脚本代码本身，不要包含任何解释、注释说明或 Markdown 代码块标记。",
            language);

        string additionalPrompt = Config.LlmAdditionalPrompt;
        if (!string.IsNullOrWhiteSpace(additionalPrompt))
        {
            basePrompt += "\n" + additionalPrompt;
        }

        return basePrompt;
    }

    /// <summary>根据 API 基础地址构建 Chat Completions 请求地址。</summary>
    /// <param name="baseUrl">已去除末尾斜杠的 API 基础地址。</param>
    /// <returns>Chat Completions 请求地址。</returns>
    private static string BuildChatCompletionsUrl(string baseUrl)
    {
        return baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : $"{baseUrl}/chat/completions";
    }

    /// <summary>规范化用户输入的 API Key，兼容误粘贴的 Bearer 前缀。</summary>
    /// <param name="apiKey">原始 API Key。</param>
    /// <returns>不包含 Bearer 前缀的 Token。</returns>
    private static string NormalizeBearerToken(string apiKey)
    {
        string trimmed = apiKey.Trim();
        const string bearerPrefix = "Bearer ";

        return trimmed.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[bearerPrefix.Length..].Trim()
            : trimmed;
    }

    /// <summary>从成功的 API 响应 JSON 中提取助手回复内容。</summary>
    /// <param name="responseJson">API 返回的完整 JSON 响应。</param>
    /// <returns>提取的文本；解析失败时返回原始 JSON。</returns>
    private static string ExtractGeneratedCode(string responseJson)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(responseJson);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("choices", out JsonElement choices)
                && choices.ValueKind is JsonValueKind.Array
                && choices.GetArrayLength() > 0)
            {
                JsonElement firstChoice = choices[0];

                if (firstChoice.TryGetProperty("message", out JsonElement message)
                    && TryReadResponseText(message, out string messageText))
                {
                    return StripMarkdownCodeBlock(messageText);
                }

                if (firstChoice.TryGetProperty("text", out JsonElement text)
                    && text.ValueKind is JsonValueKind.String)
                {
                    return StripMarkdownCodeBlock(text.GetString() ?? string.Empty);
                }

                if (firstChoice.TryGetProperty("delta", out JsonElement delta)
                    && TryReadResponseText(delta, out string deltaText))
                {
                    return StripMarkdownCodeBlock(deltaText);
                }
            }

            if (root.TryGetProperty("output_text", out JsonElement outputText)
                && outputText.ValueKind is JsonValueKind.String)
            {
                return StripMarkdownCodeBlock(outputText.GetString() ?? string.Empty);
            }
        }
        catch (JsonException)
        {
            // LLM-001: 响应结构异常，回退至原始文本
        }

        return responseJson;
    }

    /// <summary>从消息对象中读取文本内容，兼容字符串与分段数组两种 content 结构。</summary>
    /// <param name="message">消息或增量消息 JSON 对象。</param>
    /// <param name="text">读取到的文本。</param>
    /// <returns>成功读取到文本时为 true，否则为 false。</returns>
    private static bool TryReadResponseText(JsonElement message, out string text)
    {
        text = string.Empty;

        if (!message.TryGetProperty("content", out JsonElement content))
        {
            return false;
        }

        if (content.ValueKind is JsonValueKind.String)
        {
            text = content.GetString() ?? string.Empty;
            return true;
        }

        if (content.ValueKind is not JsonValueKind.Array)
        {
            return false;
        }

        StringBuilder builder = new();
        foreach (JsonElement part in content.EnumerateArray())
        {
            if (part.ValueKind is JsonValueKind.String)
            {
                builder.Append(part.GetString());
                continue;
            }

            if (part.ValueKind is JsonValueKind.Object
                && part.TryGetProperty("text", out JsonElement partText)
                && partText.ValueKind is JsonValueKind.String)
            {
                builder.Append(partText.GetString());
            }
        }

        text = builder.ToString();
        return text.Length > 0;
    }

    /// <summary>从错误响应 JSON 中提取错误消息。</summary>
    /// <param name="responseJson">API 返回的错误 JSON 响应。</param>
    /// <returns>错误消息文本；解析失败时返回原始 JSON。</returns>
    private static string ExtractErrorMessage(string responseJson)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(responseJson);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("error", out JsonElement error))
            {
                if (error.ValueKind is JsonValueKind.String)
                {
                    return error.GetString() ?? responseJson;
                }

                if (error.ValueKind is JsonValueKind.Object
                    && error.TryGetProperty("message", out JsonElement message))
                {
                    return message.GetString() ?? responseJson;
                }
            }

            if (root.TryGetProperty("message", out JsonElement rootMessage)
                && rootMessage.ValueKind is JsonValueKind.String)
            {
                return rootMessage.GetString() ?? responseJson;
            }
        }
        catch (JsonException)
        {
            // LLM-002: 错误响应非合法 JSON，回退至原始文本
        }

        return responseJson;
    }

    /// <summary>去除 Markdown 围栏代码块标记。</summary>
    /// <param name="code">待处理的代码字符串。</param>
    /// <returns>去除标记后的纯代码。</returns>
    private static string StripMarkdownCodeBlock(string code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return code;
        }

        string trimmed = code.Trim();

        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        int firstNewline = trimmed.IndexOf('\n');
        if (firstNewline >= 0)
        {
            trimmed = trimmed[(firstNewline + 1)..];
        }

        if (trimmed.EndsWith("```", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^3].TrimEnd();
        }

        return trimmed;
    }

    #endregion
}
