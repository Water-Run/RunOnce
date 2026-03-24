/*
 * LLM API 客户端
 * 提供与 OpenAI 兼容的 LLM API 交互功能，用于根据用户描述生成脚本代码
 *
 * @author: WaterRun
 * @file: Static/LlmClient.cs
 * @date: 2026-03-22
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

/// <summary>
/// LLM API 客户端，提供与 OpenAI 兼容接口的脚本生成功能。
/// </summary>
/// <remarks>
/// 不变量：HttpClient 实例全局唯一，避免套接字耗尽。
/// 线程安全：GenerateScriptAsync 可从多线程调用。
/// 副作用：发起网络请求，消耗 API 额度。
/// </remarks>
public static class LlmClient
{
    /// <summary>
    /// 全局 HttpClient 实例，复用底层连接。
    /// </summary>
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    /// <summary>
    /// 根据用户描述调用 LLM API 生成脚本代码。
    /// </summary>
    /// <param name="userPrompt">用户输入的需求描述，不允许为 null 或空白字符串。</param>
    /// <param name="preferredLanguage">用户指定的语言标识符；为 null 时由 LLM 自动选择。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>生成的脚本代码字符串。</returns>
    /// <exception cref="ArgumentNullException">当 userPrompt 为 null 时抛出。</exception>
    /// <exception cref="ArgumentException">当 userPrompt 为空白字符串时抛出。</exception>
    /// <exception cref="InvalidOperationException">当 API Key 未配置或 API 返回错误时抛出。</exception>
    /// <exception cref="TimeoutException">当请求超时时抛出。</exception>
    /// <exception cref="HttpRequestException">当网络请求失败时抛出。</exception>
    /// <exception cref="OperationCanceledException">当操作被取消时抛出。</exception>
    public static async Task<string> GenerateScriptAsync(
        string userPrompt,
        string? preferredLanguage = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userPrompt);
        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            throw new ArgumentException(Text.Localize("需求描述不能为空。"), nameof(userPrompt));
        }

        string apiKey = Config.LlmApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(Text.Localize("尚未配置 API Key，请在设置中配置 LLM API Key。"));
        }

        string baseUrl = Config.LlmBaseUrl.TrimEnd('/');
        string model = Config.LlmModel;
        int maxTokens = Config.LlmMaxTokens;

        string systemContent = BuildSystemPrompt(preferredLanguage);

        // 使用匿名对象构造请求体，通过 JsonSerializer 序列化
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

        string requestJson = JsonSerializer.Serialize(requestBody);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        // 将超时时间应用到 CancellationToken
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Config.LlmTimeoutSeconds));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
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
            string errorMessage = ExtractErrorMessage(responseJson);
            throw new InvalidOperationException(
                Text.Localize("LLM API 返回错误 ({0}): {1}", (int)response.StatusCode, errorMessage));
        }

        return ExtractGeneratedCode(responseJson);
    }

    /// <summary>
    /// 根据可选的目标语言构建系统提示词。
    /// </summary>
    private static string BuildSystemPrompt(string? preferredLanguage)
    {
        if (!string.IsNullOrEmpty(preferredLanguage))
        {
            return Text.Localize(
                "你是一个专业的脚本生成助手。根据用户的需求，使用 {0} 语言生成可执行的脚本代码。仅输出脚本代码本身，不要包含任何解释、注释说明或 Markdown 代码块标记。",
                preferredLanguage);
        }

        return Text.Localize(
            "你是一个专业的脚本生成助手。根据用户的需求生成可执行的脚本代码。仅输出脚本代码本身，不要包含任何解释、注释说明或 Markdown 代码块标记。支持的语言：bat、powershell、python、lua、nim、go。根据需求自动选择最合适的语言。");
    }

    /// <summary>
    /// 从成功的 API 响应 JSON 中提取生成的代码内容。
    /// </summary>
    private static string ExtractGeneratedCode(string responseJson)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(responseJson);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("choices", out JsonElement choices)
                && choices.ValueKind == JsonValueKind.Array
                && choices.GetArrayLength() > 0)
            {
                JsonElement firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out JsonElement message)
                    && message.TryGetProperty("content", out JsonElement content))
                {
                    string code = content.GetString() ?? string.Empty;
                    return StripMarkdownCodeBlock(code);
                }
            }
        }
        catch (JsonException)
        {
            // LLM-001: JSON 解析失败时回退，返回原始响应
        }

        return responseJson;
    }

    /// <summary>
    /// 从失败的 API 响应 JSON 中提取错误消息。
    /// </summary>
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
            // LLM-002: 错误 JSON 解析失败，返回原始文本
        }

        return responseJson;
    }

    /// <summary>
    /// 去除 LLM 可能附加的 Markdown 代码块标记（```language...```）。
    /// </summary>
    private static string StripMarkdownCodeBlock(string code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return code;
        }

        string trimmed = code.Trim();

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            int firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
            {
                trimmed = trimmed[(firstNewline + 1)..];
            }

            if (trimmed.EndsWith("```", StringComparison.Ordinal))
            {
                trimmed = trimmed[..^3].TrimEnd();
            }
        }

        return trimmed;
    }
}
