namespace ApartmentTriage.Application.Agents.Anthropic;

public sealed record AnthropicResponse(
    string Content,
    string StopReason,
    AnthropicUsage Usage);

public sealed record AnthropicUsage(
    int InputTokens,
    int OutputTokens,
    int CacheCreationInputTokens,
    int CacheReadInputTokens);
