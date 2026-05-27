using ApartmentTriage.Domain.Entities;

namespace ApartmentTriage.Application.Orchestration;

public interface ITriageOrchestrator
{
    Task<TriageResult> ProcessAsync(
        Message message,
        string preferredLanguage = "tr",
        CancellationToken cancellationToken = default);
}
