namespace ApartmentTriage.Application.Agents.Manifest;

public sealed record AgentManifest(
    string AgentId,
    string Model,
    string PromptVersion,
    string SystemPromptPath,
    AgentManifestRetry Retry);

public sealed record AgentManifestRetry(int MaxAttempts, int BackoffBaseSeconds);
