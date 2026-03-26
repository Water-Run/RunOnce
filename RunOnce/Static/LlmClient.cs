/*
 * LLM API 客户端
 * 提供与 OpenAI 兼容的 LLM API 交互功能，用于根据用户描述生成脚本代码
 *
 * @author: WaterRun
 * @file: Static/LlmClient.cs
 * @date: 2026-03-26
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

/// <summary>LLM API 客户端，封装与 OpenAI 兼容接口的脚本生成能力。</summary>
/// <remarks>
/// 不变量：<see cref="_httpClient"/> 全局唯一，复用底层 TCP 连接以避免套接字耗尽。
/// 线程安全：所有公共方法均为线程安全，每次调用无共享可变状态。
/// 副作用：发起 HTTP 请求并消耗 LLM API 额度。
/// </remarks>
public static class LlmClient
{
    /// <summary>
    /// 全局共享的 <see cref="HttpClient"/> 实例。
    /// 超时设为无限，由调用方通过 <see cref="CancellationToken"/> 控制取消与超时。
    /// 生命周期与应用程序进程一致，不可释放。
    /// </summary>
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    /// <summary>根据用户需求描述调用 LLM API 生成可执行脚本代码。</summary>
    /// <param name="userPrompt">用户输入的需求描述，不允许为 null 或空白字符串。</param>
    /// <param name="preferredLanguage">
    /// 用户指定的脚本语言标识符（如 "python"、"powershell"）；
    /// 为 null 时由 LLM 根据需求自动选择最合适的语言。
    /// </param>
    /// <param name="cancellationToken">取消令牌，用于中止正在进行的网络请求。</param>
    /// <returns>LLM 生成的纯脚本代码字符串，已去除可能附带的 Markdown 围栏标记。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="userPrompt"/> 为 null 时抛出。</exception>
    /// <exception cref="ArgumentException">当 <paramref name="userPrompt"/> 为空白字符串时抛出。</exception>
    /// <exception cref="InvalidOperationException">
    /// 当 <see cref="Config.LlmApiKey"/> 未配置时抛出；
    /// 或当 API 返回非成功状态码时抛出（消息中包含 HTTP 状态码与错误详情）。
    /// </exception>
    /// <exception cref="TimeoutException">当请求超过 <see cref="Config.LlmTimeoutSeconds"/> 配置的秒数时抛出。</exception>
    /// <exception cref="HttpRequestException">当底层网络传输失败时抛出。</exception>
    /// <exception cref="OperationCanceledException">当 <paramref name="cancellationToken"/> 被触发时抛出。</exception>
    /// <remarks>
    /// 取消语义：支持通过 <paramref name="cancellationToken"/> 取消。超时由内部链式
    /// <see cref="CancellationTokenSource"/> 控制，超时场景抛出 <see cref="TimeoutException"/>
    /// 而非 <see cref="OperationCanceledException"/>，以便调用方区分主动取消与超时。
    /// 线程/重入：可安全地从多个线程并发调用，每次调用独立且无共享可变状态。
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

        string baseUrl = Config.LlmBaseUrl.TrimEnd('/');
        string model = Config.LlmModel;
        int maxTokens = Config.LlmMaxTokens;
        string systemContent = BuildSystemPrompt(preferredLanguage);

        var requestBody = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = systemContent },
                new { role = "user", content = userPrompt },
            },
            max_tokens = maxTokens,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Config.LlmTimeoutSeconds));

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

    /// <summary>根据可选的目标语言构建 LLM 系统提示词。</summary>
    /// <param name="preferredLanguage">
    /// 用户指定的脚本语言标识符；为 null 或空时生成不限定语言的通用提示词。
    /// </param>
    /// <returns>完整的系统提示词字符串，指导 LLM 仅输出纯代码而不附带解释或标记。</returns>
    private static string BuildSystemPrompt(string? preferredLanguage)
    {
        if (!string.IsNullOrEmpty(preferredLanguage))
        {
            return Text.Localize(
                "你是一个专业的脚本生成助手。根据用户的需求，使用 {0} 语言生成可执行的脚本代码。"
                + "仅输出脚本代码本身，不要包含任何解释、注释说明或 Markdown 代码块标记。",
                preferredLanguage);
        }

        return Text.Localize(
            "你是一个专业的脚本生成助手。根据用户的需求生成可执行的脚本代码。"
            + "仅输出脚本代码本身，不要包含任何解释、注释说明或 Markdown 代码块标记。"
            + "支持的语言：bat、powershell、python、lua、nim、go。根据需求自动选择最合适的语言。");
    }

    /// <summary>
    /// 从成功的 API 响应 JSON 中提取 LLM 生成的代码内容。
    /// 当 JSON 结构不符合 OpenAI Chat Completions 格式时，回退返回原始响应文本。
    /// </summary>
    /// <param name="responseJson">API 返回的完整 JSON 响应字符串。</param>
    /// <returns>
    /// 提取并去除 Markdown 围栏标记后的纯代码字符串；
    /// 若解析失败则返回 <paramref name="responseJson"/> 原文。
    /// </returns>
    private static string ExtractGeneratedCode(string responseJson)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(responseJson);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("choices", out JsonElement choices)
                && choices.ValueKind is JsonValueKind.Array
                && choices.GetArrayLength() > 0
                && choices[0].TryGetProperty("message", out JsonElement message)
                && message.TryGetProperty("content", out JsonElement content))
            {
                return StripMarkdownCodeBlock(content.GetString() ?? string.Empty);
            }
        }
        catch (JsonException)
        {
            // LLM-001: 响应 JSON 结构异常，回退至原始文本以保持可用性
        }

        return responseJson;
    }

    /// <summary>
    /// 从失败的 API 响应 JSON 中提取人类可读的错误消息。
    /// 当 JSON 结构不包含 error.message 时，回退返回原始响应文本。
    /// </summary>
    /// <param name="responseJson">API 返回的错误 JSON 响应字符串。</param>
    /// <returns>
    /// 提取的错误消息文本；若解析失败则返回 <paramref name="responseJson"/> 原文。
    /// </returns>
    private static string ExtractErrorMessage(string responseJson)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(responseJson);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("error", out JsonElement error)
                && error.TryGetProperty("message", out JsonElement message))
            {
                return message.GetString() ?? responseJson;
            }
        }
        catch (JsonException)
        {
            // LLM-002: 错误响应非合法 JSON，回退至原始文本
        }

        return responseJson;
    }

    /// <summary>去除 LLM 可能附加的 Markdown 围栏代码块标记（```language ... ```）。</summary>
    /// <param name="code">待处理的代码字符串，可能包含 Markdown 围栏标记；允许为 null 或空。</param>
    /// <returns>去除首尾 Markdown 围栏标记后的纯代码字符串；输入为 null 或空时原样返回。</returns>
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
}