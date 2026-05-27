using ApartmentTriage.Domain.Entities;

namespace ApartmentTriage.Application.Orchestration;

public interface ITriageOrchestrator
{
    Task<TriageResult> ProcessAsync(
        Message message,
        string preferredLanguage = "tr",
        byte[]? imageData = null,
        string? imageMimeType = null,
        CancellationToken cancellationToken = default);
}
