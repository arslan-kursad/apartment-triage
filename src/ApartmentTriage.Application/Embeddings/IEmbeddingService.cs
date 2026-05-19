namespace ApartmentTriage.Application.Embeddings;

/// <summary>
/// Converts text into a fixed-dimensional embedding vector.
/// Implementation: ONNX Runtime + multilingual-e5-small (Infrastructure layer).
/// </summary>
public interface IEmbeddingService
{
    /// <summary>Returns a normalized float vector of length <see cref="Dimensions"/>.</summary>
    Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default);

    /// <summary>Output vector length. 384 for multilingual-e5-small.</summary>
    int Dimensions { get; }
}
