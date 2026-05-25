using ApartmentTriage.Application.Services;
using ApartmentTriage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ApartmentTriage.Infrastructure.Services;

public sealed class AnonymizationService : IAnonymizationService
{
    private readonly ApartmentTriageDbContext _db;
    private readonly ILogger<AnonymizationService> _logger;
    private readonly int _retentionMonths;

    public AnonymizationService(
        ApartmentTriageDbContext db,
        IConfiguration configuration,
        ILogger<AnonymizationService> logger)
    {
        _db = db;
        _logger = logger;
        _retentionMonths = configuration.GetValue<int>("Anonymization:RetentionMonths", 6);
    }

    public async Task AnonymizeResidentAsync(Guid residentId, CancellationToken ct = default)
    {
        var resident = await _db.Residents
            .Include(r => r.Messages)
            .Include(r => r.Tickets)
            .FirstOrDefaultAsync(r => r.Id == residentId, ct)
            ?? throw new InvalidOperationException($"Resident {residentId} not found.");

        // 1. Resident contact fields
        resident.Anonymize();

        // 2. Message content
        foreach (var message in resident.Messages)
            message.Anonymize();

        // 3. Ticket PII fields
        foreach (var ticket in resident.Tickets)
            ticket.RedactPii();

        // 4. Embedding vector hard delete via raw SQL (pgvector column, not tracked by EF change detection)
        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE tickets SET embedding_vector = NULL WHERE resident_id = @p0",
            [residentId],
            ct);

        // 5. VACUUM — best-effort; Neon free tier may block DDL locks
        try
        {
            await _db.Database.ExecuteSqlRawAsync("VACUUM tickets", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "VACUUM tickets failed for resident {ResidentId} — " +
                "manual VACUUM or S&C notification may be required",
                residentId);
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "KVKK anonymization completed for resident {ResidentId} " +
            "(retention policy: {RetentionMonths} months)",
            residentId, _retentionMonths);
    }
}
