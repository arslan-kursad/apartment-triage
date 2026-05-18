namespace ApartmentTriage.Application.Agents.Anthropic;

public interface IAnthropicClient
{
    Task<AnthropicResponse> CompleteAsync(AnthropicRequest request, CancellationToken cancellationToken = default);
}
