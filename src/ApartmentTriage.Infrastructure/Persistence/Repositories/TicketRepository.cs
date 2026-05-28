using ApartmentTriage.Application.Agents.Enricher;
using ApartmentTriage.Application.Repositories;
using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Pgvector;

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

    public async Task<IReadOnlyList<SimilarTicket>> FindSimilarAsync(
        float[] vector, Guid excludeTicketId, int topK = 5,
        CancellationToken cancellationToken = default)
    {
        var pgVector = new Vector(vector);

        // Raw SQL: EF Core LINQ operators for vector_cosine_ops require the entity
        // property to be Vector type; Domain uses float[] so we use raw SQL here.
        var sql = $"""
            SELECT id, category, created_at,
                   1 - (embedding_vector <=> @vector::vector) AS cosine_similarity
            FROM tickets
            WHERE embedding_vector IS NOT NULL
              AND id != @excludeId
            ORDER BY embedding_vector <=> @vector::vector
            LIMIT @topK
            """;

        var results = await _db.Database
            .SqlQueryRaw<SimilarTicketRow>(sql,
                new Npgsql.NpgsqlParameter("vector", pgVector),
                new Npgsql.NpgsqlParameter("excludeId", excludeTicketId),
                new Npgsql.NpgsqlParameter("topK", topK))
            .ToListAsync(cancellationToken);

        return results
            .Select(r => new SimilarTicket(r.Id, r.Category, r.CosineSimilarity, r.CreatedAt))
            .ToList();
    }

    public async Task<Ticket?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await _db.Tickets
            .Include(t => t.SourceMessage)
            .Include(t => t.Resident)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<Ticket> Items, int TotalCount)> GetPagedAsync(
        TicketStatus? status,
        TicketCategory? category,
        bool? isEmergency,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Tickets.AsQueryable();

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);
        if (category.HasValue)
            query = query.Where(t => t.Category == category.Value);
        if (isEmergency.HasValue)
            query = query.Where(t => t.IsEmergency == isEmergency.Value);

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .Include(t => t.SourceMessage)
            .Include(t => t.Resident)
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    // Projection type for SqlQueryRaw — not an entity, stays in Infrastructure.
    private sealed class SimilarTicketRow
    {
        public Guid Id { get; set; }
        public Domain.Enums.TicketCategory Category { get; set; }
        public double CosineSimilarity { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
