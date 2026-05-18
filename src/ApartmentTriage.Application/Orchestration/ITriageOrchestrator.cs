using ApartmentTriage.Domain.Entities;

namespace ApartmentTriage.Application.Orchestration;

public interface ITriageOrchestrator
{
    Task<TriageResult> ProcessAsync(Message message, CancellationToken cancellationToken = default);
}
