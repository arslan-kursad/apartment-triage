using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;

namespace ApartmentTriage.Application.Orchestration;

public interface ITriageOrchestrator
{
    Task<TriageResult> ProcessAsync(
        Message message,
        string preferredLanguage = "tr",
        byte[]? imageData = null,
        string? imageMimeType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-classifies and re-routes an existing ticket using combined original + clarification text.
    /// Called when a clarification response arrives and the ticket had <see cref="AmbiguityReason.CategoryAmbiguous"/>.
    /// Mutates the ticket in-place and persists changes.
    /// </summary>
    Task<TriageResult> ReclassifyTicketAsync(
        Ticket ticket,
        string combinedText,
        string preferredLanguage = "tr",
        CancellationToken cancellationToken = default);
}
