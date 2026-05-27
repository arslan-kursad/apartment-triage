using ApartmentTriage.Application.Services;

namespace ApartmentTriage.Infrastructure.Services;

/// <summary>
/// Fallback implementation used when WhisperTranscriptionService fails to initialize
/// (e.g. missing native runtime library). Always returns an empty string so the caller
/// can detect the failure and inform the resident rather than crashing the pipeline.
/// </summary>
internal sealed class NoopTranscriptionService : ITranscriptionService
{
    public Task<string> TranscribeAsync(Stream audio, string lang = "tr", CancellationToken cancellationToken = default)
        => Task.FromResult(string.Empty);
}
