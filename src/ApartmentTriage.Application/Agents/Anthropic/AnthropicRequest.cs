namespace ApartmentTriage.Application.Agents.Anthropic;

public sealed record AnthropicRequest(
    string Model,
    string SystemPrompt,
    IReadOnlyList<AnthropicMessage> Messages,
    int MaxTokens = 1024,
    bool CacheSystemPrompt = true);

public sealed record AnthropicMessage(string Role, string Content);
