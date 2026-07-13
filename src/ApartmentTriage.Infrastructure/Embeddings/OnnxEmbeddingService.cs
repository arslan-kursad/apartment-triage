using ApartmentTriage.Application.Embeddings;
using BlingFire;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ApartmentTriage.Infrastructure.Embeddings;

/// <summary>
/// Runs multilingual-e5-small via ONNX Runtime.
/// Singleton — InferenceSession is thread-safe and expensive to create.
/// </summary>
public sealed class OnnxEmbeddingService : IEmbeddingService, IDisposable
{
    private const int MaxTokens = 128;

    // XLM-RoBERTa special-token IDs (fairseq layout) — the sentence-piece IDs from
    // BlingFire are wrapped with these to match what the ONNX model was trained on.
    private const int BosId = 0;   // <s>
    private const int EosId = 2;   // </s>
    private const int UnkId = 3;   // <unk>

    private readonly InferenceSession _session;
    private readonly ulong _tokenizerHandle;

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

        // BlingFire's xlm_roberta_base.bin ships in the BlingFireNuget package and is
        // copied next to this assembly at build time (see Infrastructure.csproj). It
        // reproduces the real XLM-RoBERTa SentencePiece token IDs — the character-level
        // placeholder this replaced produced semantically meaningless embeddings (ADR-0016).
        var tokenizerModelPath = Path.Combine(AppContext.BaseDirectory, "xlm_roberta_base.bin");
        if (!File.Exists(tokenizerModelPath))
            throw new FileNotFoundException(
                $"XLM-R tokenizer model not found at '{tokenizerModelPath}'. " +
                "It ships with the BlingFireNuget package; check that it was copied to output.",
                tokenizerModelPath);

        _tokenizerHandle = BlingFireUtils.LoadModel(tokenizerModelPath);
        if (_tokenizerHandle == 0)
            throw new InvalidOperationException(
                $"BlingFire failed to load tokenizer model '{tokenizerModelPath}'.");
    }

    public Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // multilingual-e5-small requires a task-instruction prefix on all input text.
        // This system only ever computes symmetric complaint-to-complaint similarity —
        // TriageOrchestrator persists the same vector EnricherAgent computes for an
        // incoming complaint as that ticket's permanent stored representation (see
        // TriageOrchestrator.cs SetEmbeddingVector calls), so every "past ticket" vector
        // was itself a live complaint embedding at creation time. There is no separate
        // index-time passage step to justify e5's asymmetric query/passage split; the
        // model card's guidance for symmetric tasks (STS, similarity, clustering)
        // applies instead: prefix uniformly with "query: ".
        var tokenIds = Tokenize($"query: {text}");
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
    // Real XLM-RoBERTa SentencePiece tokenization via BlingFire's xlm_roberta_base.bin.
    // BlingFire emits the inner sub-word IDs (verified byte-exact against HuggingFace's
    // XLMRobertaTokenizer — ADR-0016); we wrap them with <s>(0) … </s>(2), matching the
    // special-token layout the ONNX model expects. Truncation keeps room for </s>.
    private long[] Tokenize(string text)
    {
        var utf8 = System.Text.Encoding.UTF8.GetBytes(text);

        // BlingFire needs a caller-sized output buffer; worst case one id per byte.
        var buffer = new int[utf8.Length + 1];
        var count = BlingFireUtils.TextToIds(_tokenizerHandle, utf8, utf8.Length, buffer, buffer.Length, UnkId);
        if (count < 0) count = 0;

        // Leave room for the two special tokens; truncate the sub-word ids if needed.
        var innerCount = Math.Min(count, MaxTokens - 2);

        var ids = new long[innerCount + 2];
        ids[0] = BosId;
        for (var i = 0; i < innerCount; i++)
            ids[i + 1] = buffer[i];
        ids[innerCount + 1] = EosId;
        return ids;
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

    public void Dispose()
    {
        _session.Dispose();
        if (_tokenizerHandle != 0)
            BlingFireUtils.FreeModel(_tokenizerHandle);
    }
}
