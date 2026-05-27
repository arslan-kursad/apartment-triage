namespace ApartmentTriage.Application.Services;

/// <summary>
/// Transcribes an audio stream to text.
/// Infrastructure implementation: WhisperTranscriptionService (Whisper.net, ggml-base model).
/// </summary>
public interface ITranscriptionService
{
    /// <summary>
    /// Transcribes the audio stream.
    /// </summary>
    /// <param name="audio">Audio stream. Telegram Voice messages are OGG Opus — runtime must support the format.</param>
    /// <param name="lang">BCP-47 language hint ("tr" or "en"). Passed to the Whisper model for better accuracy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Transcribed text, or empty string if no speech detected.</returns>
    Task<string> TranscribeAsync(Stream audio, string lang = "tr", CancellationToken cancellationToken = default);
}
