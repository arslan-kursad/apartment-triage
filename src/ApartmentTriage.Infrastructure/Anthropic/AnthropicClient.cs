using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ApartmentTriage.Application.Agents.Anthropic;
using Microsoft.Extensions.Logging;

namespace ApartmentTriage.Infrastructure.Anthropic;

public sealed class AnthropicClient : IAnthropicClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AnthropicClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AnthropicClient(IHttpClientFactory httpClientFactory, ILogger<AnthropicClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<AnthropicResponse> CompleteAsync(
        AnthropicRequest request, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("anthropic");
        var body = BuildRequestBody(request);

        var response = await client.PostAsJsonAsync("messages", body, JsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var statusCode = (int)response.StatusCode;
            var body2 = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Anthropic API error {StatusCode}: {Body}", statusCode, body2);
            response.EnsureSuccessStatusCode(); // throws HttpRequestException with status
        }

        var raw = await response.Content.ReadFromJsonAsync<AnthropicRawResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Empty response from Anthropic API");

        var content = raw.Content?.FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("No text content in Anthropic response");

        return new AnthropicResponse(
            content,
            raw.StopReason ?? "unknown",
            new AnthropicUsage(
                raw.Usage?.InputTokens ?? 0,
                raw.Usage?.OutputTokens ?? 0,
                raw.Usage?.CacheCreationInputTokens ?? 0,
                raw.Usage?.CacheReadInputTokens ?? 0));
    }

    // When CacheSystemPrompt is true, wraps the system prompt in a content block array
    // with cache_control so Anthropic's prompt caching activates on repeated calls.
    private static object BuildRequestBody(AnthropicRequest request)
    {
        object systemContent = request.CacheSystemPrompt
            ? (object)new[]
            {
                new
                {
                    type = "text",
                    text = request.SystemPrompt,
                    cache_control = new { type = "ephemeral" }
                }
            }
            : request.SystemPrompt;

        return new
        {
            model = request.Model,
            system = systemContent,
            messages = request.Messages.Select(m => new { role = m.Role, content = m.Content }),
            max_tokens = request.MaxTokens
        };
    }

    private sealed class AnthropicRawResponse
    {
        public List<AnthropicContentBlock>? Content { get; set; }
        public string? StopReason { get; set; }
        public AnthropicRawUsage? Usage { get; set; }
    }

    private sealed class AnthropicContentBlock
    {
        public string? Text { get; set; }
    }

    private sealed class AnthropicRawUsage
    {
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public int CacheCreationInputTokens { get; set; }
        public int CacheReadInputTokens { get; set; }
    }
}
