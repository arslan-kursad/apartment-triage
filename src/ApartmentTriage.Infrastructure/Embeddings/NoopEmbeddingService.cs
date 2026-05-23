using ApartmentTriage.Application.Embeddings;

namespace ApartmentTriage.Infrastructure.Embeddings;

/// <summary>
/// Development-only fallback used when ONNX native runtime is unavailable locally.
/// </summary>
public sealed class NoopEmbeddingService : IEmbeddingService
{
    public int Dimensions => 384;

    public Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Array.Empty<float>());
    }
}
