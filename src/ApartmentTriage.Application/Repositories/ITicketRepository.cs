using ApartmentTriage.Application.Agents.Enricher;
using ApartmentTriage.Domain.Entities;

namespace ApartmentTriage.Application.Repositories;

public interface ITicketRepository
{
    Task AddAsync(Ticket ticket, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<Ticket> tickets, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SimilarTicket>> FindSimilarAsync(
        float[] vector, Guid excludeTicketId, int topK = 5,
        CancellationToken cancellationToken = default);
}
