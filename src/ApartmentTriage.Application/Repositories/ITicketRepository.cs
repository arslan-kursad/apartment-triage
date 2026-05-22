using ApartmentTriage.Application.Agents.Enricher;
using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;

namespace ApartmentTriage.Application.Repositories;

public interface ITicketRepository
{
    Task AddAsync(Ticket ticket, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<Ticket> tickets, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SimilarTicket>> FindSimilarAsync(
        float[] vector, Guid excludeTicketId, int topK = 5,
        CancellationToken cancellationToken = default);

    /// <summary>Returns ticket with SourceMessage and Resident navigations included.</summary>
    Task<Ticket?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a sorted (CreatedAt desc) page with total count for pagination.
    /// All filter params are optional — null means "no filter".
    /// </summary>
    Task<(IReadOnlyList<Ticket> Items, int TotalCount)> GetPagedAsync(
        TicketStatus? status,
        TicketCategory? category,
        bool? isEmergency,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
