using ApartmentTriage.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Whisper.net;

namespace ApartmentTriage.Infrastructure.Services;

/// <summary>
/// Transcribes audio using Whisper.net (ggml-base, ~142 MB, multilingual).
/// The WhisperFactory is loaded once at construction and reused across calls.
/// Each call creates a short-lived WhisperProcessor (not thread-safe).
/// Input audio should be WAV/PCM; Telegram .ogg Opus requires runtime OGG support.
/// </summary>
public sealed class WhisperTranscriptionService : ITranscriptionService, IDisposable
{
    private readonly WhisperFactory _factory;
    private readonly ILogger<WhisperTranscriptionService> _logger;

    public WhisperTranscriptionService(
        IOptions<WhisperOptions> options,
        ILogger<WhisperTranscriptionService> logger)
    {
        _logger = logger;
        var modelPath = options.Value.ModelPath;

        if (!File.Exists(modelPath))
            throw new InvalidOperationException(
                $"Whisper model not found at '{modelPath}'. " +
                "Run scripts/download-models.sh to download whisper-base.bin.");

        _factory = WhisperFactory.FromPath(modelPath);
        _logger.LogInformation("WhisperFactory loaded from {ModelPath}", modelPath);
    }

    public async Task<string> TranscribeAsync(
        Stream audio,
        string lang = "tr",
        CancellationToken cancellationToken = default)
    {
        // Whisper.net maps language hints; unmapped codes fall back to auto-detect.
        var whisperLang = lang switch
        {
            "tr" => "tr",
            "en" => "en",
            _    => "auto"
        };

        using var processor = _factory.CreateBuilder()
            .WithLanguage(whisperLang)
            .Build();

        var segments = new System.Text.StringBuilder();

        await foreach (var segment in processor.ProcessAsync(audio, cancellationToken))
        {
            segments.Append(segment.Text);
        }

        var transcript = segments.ToString().Trim();

        _logger.LogInformation(
            "Transcription complete (lang={Language}, chars={Length})",
            whisperLang, transcript.Length);

        return transcript;
    }

    public void Dispose() => _factory.Dispose();
}

/// <summary>Configuration for WhisperTranscriptionService.</summary>
public sealed class WhisperOptions
{
    public const string SectionName = "Whisper";

    /// <summary>Absolute path to ggml-base.bin (whisper-base model). ~142 MB.</summary>
    public string ModelPath { get; set; } = "/app/models/whisper/whisper-base.bin";
}
