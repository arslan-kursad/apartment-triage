namespace ApartmentTriage.Application.Services;

public interface IAnonymizationService
{
    /// <summary>
    /// Anonymizes all PII for the given resident: contact fields, message content,
    /// and ticket PII. Embedding vectors are hard-deleted via raw SQL.
    /// Irreversible — call only after KVKK retention period expires.
    /// </summary>
    Task AnonymizeResidentAsync(Guid residentId, CancellationToken ct = default);
}
