using ApartmentTriage.Application.Embeddings;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ApartmentTriage.Infrastructure.Embeddings;

/// <summary>
/// Runs multilingual-e5-small via ONNX Runtime.
/// Singleton — InferenceSession is thread-safe and expensive to create.
/// </summary>
public sealed class OnnxEmbeddingService : IEmbeddingService, IDisposable
{
    private readonly InferenceSession _session;

    public int Dimensions => 384;

    public OnnxEmbeddingService(string modelPath)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException(
                $"ONNX model not found at '{modelPath}'. " +
                "Run scripts/download-models.sh to download the model.",
                modelPath);

        var opts = new SessionOptions { InterOpNumThreads = 1, IntraOpNumThreads = 2 };
        _session = new InferenceSession(modelPath, opts);
    }

    public Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var tokenIds = Tokenize(text);
        var seqLen = tokenIds.Length;

        var attentionMask = Enumerable.Repeat(1L, seqLen).ToArray();
        var tokenTypeIds = new long[seqLen];

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(
                "input_ids",
                new DenseTensor<long>(new Memory<long>(tokenIds), [1, seqLen])),
            NamedOnnxValue.CreateFromTensor(
                "attention_mask",
                new DenseTensor<long>(new Memory<long>(attentionMask), [1, seqLen])),
            NamedOnnxValue.CreateFromTensor(
                "token_type_ids",
                new DenseTensor<long>(new Memory<long>(tokenTypeIds), [1, seqLen])),
        };

        using var results = _session.Run(inputs);

        var hidden = results
            .First(r => r.Name == "last_hidden_state")
            .AsTensor<float>();

        var embedding = MeanPool(hidden, attentionMask, seqLen);
        return Task.FromResult(L2Normalize(embedding));
    }

    // ── Tokenizer ─────────────────────────────────────────────────────────────
    // PLACEHOLDER: character-level mapping produces valid tensor shapes but
    // semantically approximate embeddings.
    // Replace with proper XLM-RoBERTa SentencePiece tokenizer before Day 14 deploy.
    // Pending: tokenizer library Architect approval (separate flag).
    private static long[] Tokenize(string text, int maxLength = 128)
    {
        const long Cls = 0L;
        const long Sep = 2L;

        var ids = new List<long>(maxLength) { Cls };

        foreach (var ch in text.Normalize().Take(maxLength - 2))
        {
            // Map each character to a stable vocab range [100, 50099]
            // avoiding reserved special token IDs (0-99).
            ids.Add((long)(ch % 50_000) + 100L);
        }

        ids.Add(Sep);
        return ids.ToArray();
    }

    // ── Pooling + normalization ───────────────────────────────────────────────

    private static float[] MeanPool(Tensor<float> hidden, long[] mask, int seqLen)
    {
        var result = new float[384];
        int unmasked = 0;

        for (int i = 0; i < seqLen; i++)
        {
            if (mask[i] == 0L) continue;
            unmasked++;
            for (int j = 0; j < 384; j++)
                result[j] += hidden[0, i, j];
        }

        if (unmasked > 0)
            for (int j = 0; j < 384; j++)
                result[j] /= unmasked;

        return result;
    }

    private static float[] L2Normalize(float[] v)
    {
        float norm = MathF.Sqrt(v.Sum(x => x * x));
        return norm == 0f ? v : v.Select(x => x / norm).ToArray();
    }

    public void Dispose() => _session.Dispose();
}
