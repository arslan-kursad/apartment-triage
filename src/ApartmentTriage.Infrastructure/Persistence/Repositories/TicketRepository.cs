using ApartmentTriage.Application.Repositories;
using ApartmentTriage.Domain.Entities;

namespace ApartmentTriage.Infrastructure.Persistence.Repositories;

public sealed class TicketRepository : ITicketRepository
{
    private readonly ApartmentTriageDbContext _db;

    public TicketRepository(ApartmentTriageDbContext db) => _db = db;

    public Task AddAsync(Ticket ticket, CancellationToken cancellationToken = default)
    {
        _db.Tickets.Add(ticket);
        return Task.CompletedTask;
    }

    public Task AddRangeAsync(IEnumerable<Ticket> tickets, CancellationToken cancellationToken = default)
    {
        _db.Tickets.AddRange(tickets);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);
}
