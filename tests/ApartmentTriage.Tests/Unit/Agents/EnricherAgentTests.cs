using ApartmentTriage.Application.Agents;
using ApartmentTriage.Application.Agents.Enricher;
using ApartmentTriage.Application.Embeddings;
using ApartmentTriage.Application.Repositories;
using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApartmentTriage.Tests.Unit.Agents;

public sealed class EnricherAgentTests
{
    [Fact]
    public async Task ExecuteAsync_WhenEmbeddingUnavailable_ReturnsLowConfidenceWithoutSimilaritySearch()
    {
        var repository = new ThrowingTicketRepository();
        var agent = new EnricherAgent(
            new EmptyEmbeddingService(),
            repository,
            NullLogger<EnricherAgent>.Instance);

        var residentId = Guid.NewGuid();
        var input = new EnricherInput(
            TicketId: Guid.NewGuid(),
            ResidentId: residentId,
            RawText: "Asansor calismiyor.",
            ClassifiedCategory: TicketCategory.Elevator);

        var context = new AgentContext(
            CorrelationId: Guid.NewGuid(),
            MessageId: Guid.NewGuid(),
            ResidentId: residentId,
            ReceivedAt: DateTimeOffset.UtcNow);

        var result = await agent.ExecuteAsync(input, context);

        result.IsSuccess.Should().BeTrue();
        result.Value!.EmbeddingVector.Should().BeEmpty();
        result.Value.SimilarTickets.Should().BeEmpty();
        result.Value.ConfidenceLevel.Should().Be(ConfidenceLevel.Low);
        repository.FindSimilarCalled.Should().BeFalse();
    }

    private sealed class EmptyEmbeddingService : IEmbeddingService
    {
        public int Dimensions => 384;

        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
            => Task.FromResult(Array.Empty<float>());
    }

    private sealed class ThrowingTicketRepository : ITicketRepository
    {
        public bool FindSimilarCalled { get; private set; }

        public Task AddAsync(Ticket ticket, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task AddRangeAsync(IEnumerable<Ticket> tickets, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<SimilarTicket>> FindSimilarAsync(
            float[] vector,
            Guid excludeTicketId,
            int topK = 5,
            CancellationToken cancellationToken = default)
        {
            FindSimilarCalled = true;
            throw new InvalidOperationException("Similarity search should not run without an embedding vector.");
        }

        public Task<Ticket?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<Ticket?>(null);

        public Task<(IReadOnlyList<Ticket> Items, int TotalCount)> GetPagedAsync(
            TicketStatus? status,
            TicketCategory? category,
            bool? isEmergency,
            Guid? residentId,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
            => Task.FromResult<(IReadOnlyList<Ticket>, int)>(([], 0));
    }
}
