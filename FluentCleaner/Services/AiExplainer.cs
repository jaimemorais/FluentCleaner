using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentCleaner.Models;

namespace FluentCleaner.Services;

// Explains Winapp2 entries through the provider selected in Settings.
// Results are cached per provider and rule.
public static class AiExplainer
{
    private static readonly HttpClient _http = new();
    private static readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

    private static (string BaseUrl, string Model) ProviderInfo(string provider) => provider switch
    {
        "OpenAI"    => ("https://api.openai.com/v1/chat/completions", "gpt-4o-mini"),
        "Anthropic" => ("https://api.anthropic.com/v1/messages", "claude-haiku-4-5-20251001"),
        _           => ("https://api.groq.com/openai/v1/chat/completions", "openai/gpt-oss-120b"),
    };

    private static string? ApiKey(string provider) => provider switch
    {
        "OpenAI"    => AppSettings.Instance.OpenAiApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
        "Anthropic" => AppSettings.Instance.AnthropicApiKey ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"),
        _           => AppSettings.Instance.GroqApiKey ?? Environment.GetEnvironmentVariable("GROQ_API_KEY"),
    };

    private static bool IsAnthropic(string provider) => provider == "Anthropic";

    public static bool HasConfiguredKey =>
        !string.IsNullOrWhiteSpace(ApiKey(AppSettings.Instance.AiProvider));

    private static string ProviderMessage(string key, string provider, string detail)
    {
        var template = ResourceService.Get(key);
        return template.Contains("{1}", StringComparison.Ordinal)
            ? string.Format(template, provider, detail)
            : $"{provider}: {detail}";
    }

    public static async Task<string> ExplainAsync(CleanerEntry entry)
    {
        var provider = AppSettings.Instance.AiProvider;
        var cacheKey = $"{provider}\n{entry.RawText}";
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var apiKey = ApiKey(provider);
        if (string.IsNullOrWhiteSpace(apiKey))
            return $"{provider}: {ResourceService.Get("AI_NoKeyShort")}";

        var systemPrompt = "You are a Windows PC expert. Explain Winapp2 cleaner entries concisely and accurately in English based on the file paths and registry keys provided.";
        var (text, error) = await SendChatAsync(provider, apiKey!, systemPrompt, BuildPrompt(entry), 300);
        if (error != null)
            return ProviderMessage("AI_ApiError", provider, error);

        _cache[cacheKey] = text!;
        return text!;
    }

    // A small real request verifies both the key and the selected provider.
    public static async Task<string> TestKeyAsync(string apiKey, string provider)
    {
        var userMsg = "Describe FluentCleaner by Belim (builtbybel) in 2 short sentences. " +
            "Facts: open-source, built solo, written in C# on .NET 10 and WinUI 3, native Windows UI, " +
            "no telemetry, uses the Winapp2 database. Do NOT mention AI, machine learning or any " +
            "AI-related features. Keep it factual and respond in English.";

        var (text, error) = await SendChatAsync(provider, apiKey, systemPrompt: null, userMsg, maxTokens: 512);
        return error != null ? $"✗ {error}" : $"✓ {text}";
    }

    // Groq and OpenAI use chat completions; Anthropic uses its own message shape.
    private static async Task<(string? Text, string? Error)> SendChatAsync(
        string provider, string apiKey, string? systemPrompt, string userMsg, int maxTokens)
    {
        var (baseUrl, model) = ProviderInfo(provider);

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl);

            object body;
            if (IsAnthropic(provider))
            {
                req.Headers.Add("x-api-key", apiKey);
                req.Headers.Add("anthropic-version", "2023-06-01");
                var messages = new[] { new { role = "user", content = userMsg } };
                body = systemPrompt is null
                    ? new { model, max_tokens = maxTokens, messages }
                    : new { model, max_tokens = maxTokens, system = systemPrompt, messages };
            }
            else
            {
                req.Headers.Add("Authorization", $"Bearer {apiKey}");
                var messages = systemPrompt == null
                    ? new[] { new { role = "user", content = userMsg } }
                    : new[] { new { role = "system", content = systemPrompt }, new { role = "user", content = userMsg } };

                if (provider == "Groq")
                {
                    body = new
                    {
                        model,
                        max_completion_tokens = maxTokens,
                        reasoning_effort = "low",
                        reasoning_format = "hidden",
                        messages
                    };
                }
                else
                {
                    body = new { model, max_tokens = maxTokens, messages };
                }
            }

            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var res = await _http.SendAsync(req);
            var json = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var err))
            {
                var msg = err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var m)
                    ? m.GetString() : err.GetString();
                return (null, msg ?? "Unknown error");
            }

            var text = IsAnthropic(provider)
                ? root.GetProperty("content")[0].GetProperty("text").GetString()
                : root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

            return string.IsNullOrWhiteSpace(text)
                ? (null, ResourceService.Get("AI_NoResponse"))
                : (text, null);
        }
        catch (Exception ex)
        {
            return (null, ProviderMessage("AI_NetworkError", provider, ex.Message));
        }
    }

    // Include the real paths so the explanation is about this rule, not its title alone.
    private static string BuildPrompt(CleanerEntry entry)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Explain what the Winapp2 cleaner entry \"{entry.Name}\" cleans and whether it is safe to delete.");

        if (!string.IsNullOrWhiteSpace(entry.Warning))
            sb.AppendLine($"Warning from the database: {entry.Warning}");

        if (entry.FileKeys.Count > 0)
        {
            sb.AppendLine("It deletes files from these locations:");
            foreach (var fk in entry.FileKeys.Take(6))
                sb.AppendLine($"  - {fk.Path}  (pattern: {fk.Pattern})");
        }

        if (entry.RegKeys.Count > 0)
        {
            sb.AppendLine("It removes these registry keys:");
            foreach (var rk in entry.RegKeys.Take(4))
                sb.AppendLine($"  - {rk.KeyPath}");
        }

        sb.AppendLine("Answer in 2-3 sentences. Be specific and practical.");
        return sb.ToString();
    }
}
