using ApartmentTriage.Application.Agents;
using ApartmentTriage.Application.Agents.Enricher;
using ApartmentTriage.Application.Repositories;
using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;
using ApartmentTriage.Infrastructure.Embeddings;
using ApartmentTriage.Infrastructure.Persistence;
using ApartmentTriage.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Pgvector.Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace ApartmentTriage.Tests.Eval;

// Shared fixture: starts one pgvector container and seeds it once for all integration tests.
// Model path absent → InitializeAsync returns early → all tests skip via null check.
public sealed class EnricherDbFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private NpgsqlDataSource? _dataSource;

    public string? ModelPath { get; private set; }
    public string? ConnectionString { get; private set; }

    public async Task InitializeAsync()
    {
        ModelPath = Environment.GetEnvironmentVariable("EMBEDDINGS__MODELPATH");
        if (ModelPath == null) return;

        _container = new PostgreSqlBuilder()
            .WithImage("pgvector/pgvector:pg16")
            .Build();

        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        // Vector type must be registered on the ADO.NET data source, not just EF's
        // LINQ-level UseVector() below — same gap DependencyInjection.AddInfrastructure
        // documents for production. Without it, writing a Pgvector.Vector parameter
        // (SaveChangesAsync in SeedAsync) fails with "Cannot resolve 'vector' to a
        // fully qualified datatype name."
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(ConnectionString);
        dataSourceBuilder.UseVector();
        _dataSource = dataSourceBuilder.Build();

        await using var ctx = CreateDbContext();
        await ctx.Database.MigrateAsync();
        await SeedAsync(ctx);
    }

    public async Task DisposeAsync()
    {
        if (_dataSource != null) await _dataSource.DisposeAsync();
        if (_container != null) await _container.DisposeAsync();
    }

    public ApartmentTriageDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApartmentTriageDbContext>()
            .UseNpgsql(_dataSource!, npgsql => npgsql.UseVector())
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ApartmentTriageDbContext(options);
    }

    private async Task SeedAsync(ApartmentTriageDbContext ctx)
    {
        using var onnx = new OnnxEmbeddingService(ModelPath!);

        var resident = Resident.Create(whatsAppNumber: "+905550000001");
        ctx.Residents.Add(resident);
        await ctx.SaveChangesAsync();

        // 5 plumbing seed tickets
        var plumbingTexts = new[]
        {
            "Banyo musluğu damlıyor, tamir istiyorum.",
            "Mutfak lavabosunda su kaçağı var, tavan ıslanıyor.",
            "Tuvalet sifonu çalışmıyor, sürekli su akıyor.",
            "Su borusu çatlak, duvarlar tamamen ıslanıyor.",
            "Musluk tamire ihtiyaç duyuyor, her gece damlıyor."
        };

        for (var i = 0; i < plumbingTexts.Length; i++)
        {
            var msg = Message.Create(
                residentId: resident.Id,
                channelType: ChannelType.WhatsApp,
                externalMessageId: $"seed-plumbing-{i:D3}",
                rawText: plumbingTexts[i],
                receivedAt: DateTime.UtcNow);
            ctx.Messages.Add(msg);
            await ctx.SaveChangesAsync();

            var ticket = Ticket.Create(
                residentId: resident.Id,
                sourceMessageId: msg.Id,
                category: TicketCategory.Plumbing,
                severity: TicketSeverity.Low,
                categoryConfidence: ConfidenceLevel.High,
                emergencyConfidence: ConfidenceLevel.Low);
            ticket.SetEmbeddingVector(await onnx.GetEmbeddingAsync(plumbingTexts[i]));
            ctx.Tickets.Add(ticket);
            await ctx.SaveChangesAsync();
        }

        // 2 elevator seed tickets
        var elevatorTexts = new[]
        {
            "Asansör çalışmıyor, yaşlı annem dışarı çıkamıyor.",
            "Asansör kapıları düzgün kapanmıyor, tehlikeli durum."
        };

        for (var i = 0; i < elevatorTexts.Length; i++)
        {
            var msg = Message.Create(
                residentId: resident.Id,
                channelType: ChannelType.WhatsApp,
                externalMessageId: $"seed-elevator-{i:D3}",
                rawText: elevatorTexts[i],
                receivedAt: DateTime.UtcNow);
            ctx.Messages.Add(msg);
            await ctx.SaveChangesAsync();

            var ticket = Ticket.Create(
                residentId: resident.Id,
                sourceMessageId: msg.Id,
                category: TicketCategory.Elevator,
                severity: TicketSeverity.Medium,
                categoryConfidence: ConfidenceLevel.High,
                emergencyConfidence: ConfidenceLevel.Low);
            ticket.SetEmbeddingVector(await onnx.GetEmbeddingAsync(elevatorTexts[i]));
            ctx.Tickets.Add(ticket);
            await ctx.SaveChangesAsync();
        }
    }
}

public sealed class EnricherEvalTests : IClassFixture<EnricherDbFixture>
{
    private readonly EnricherDbFixture _fixture;

    public EnricherEvalTests(EnricherDbFixture fixture) => _fixture = fixture;

    // ── ec-0019: Cold start — Category=Eval, runs in CI ──────────────────────────
    // Empty DB (mock repo). Verifies agent handles zero similar tickets gracefully.

    [Fact, Trait("Category", "Eval")]
    public async Task ec0019_ColdStart_EmptyDb_ReturnsZeroAndLowConfidence()
    {
        var modelPath = Environment.GetEnvironmentVariable("EMBEDDINGS__MODELPATH");
        if (modelPath == null) return; // silent skip — same pattern as ClassifierEvalTests

        using var onnx = new OnnxEmbeddingService(modelPath);
        var agent = new EnricherAgent(onnx, new EmptyTicketRepository(), NullLogger<EnricherAgent>.Instance);

        var residentId = Guid.NewGuid();
        var input = new EnricherInput(
            TicketId: Guid.NewGuid(),
            ResidentId: residentId,
            RawText: "Banyo musluğu damlıyor, tamir istiyorum.",
            ClassifiedCategory: TicketCategory.Plumbing);

        var ctx = new AgentContext(
            CorrelationId: Guid.NewGuid(),
            MessageId: Guid.NewGuid(),
            ResidentId: residentId,
            ReceivedAt: DateTimeOffset.UtcNow);

        var result = await agent.ExecuteAsync(input, ctx);

        result.IsSuccess.Should().BeTrue("ec-0019: cold start should not fail");

        var output = result.Value!;
        var countPass = output.SimilarTickets.Count >= 0;
        var confPass  = output.ConfidenceLevel == ConfidenceLevel.Low;

        PrintResult("ec-0019", output.SimilarTickets.Count, minCount: 0,
            output.ConfidenceLevel, ConfidenceLevel.Low);

        output.SimilarTickets.Count.Should().Be(0, "ec-0019: no tickets in DB → zero results");
        output.ConfidenceLevel.Should().Be(ConfidenceLevel.Low, "ec-0019: empty result → Low");

        _ = countPass; _ = confPass; // suppress unused-var warning
    }

    // ── ec-0017: Plumbing input — Category=EnricherIntegration ───────────────────
    // Seeded DB has 5 plumbing + 2 elevator tickets.
    // Similar plumbing input should surface ≥1 ticket at High confidence.

    [Fact, Trait("Category", "EnricherIntegration")]
    public async Task ec0017_PlumbingInput_FindsSimilarPlumbingTickets()
    {
        if (_fixture.ModelPath == null) return;

        using var onnx = new OnnxEmbeddingService(_fixture.ModelPath);
        await using var db = _fixture.CreateDbContext();
        var agent = new EnricherAgent(onnx, new TicketRepository(db), NullLogger<EnricherAgent>.Instance);

        var residentId = Guid.NewGuid();
        var input = new EnricherInput(
            TicketId: Guid.NewGuid(),
            ResidentId: residentId,
            RawText: "Banyo musluğu arızalı, sürekli su sızdırıyor.",
            ClassifiedCategory: TicketCategory.Plumbing);

        var ctx = new AgentContext(
            CorrelationId: Guid.NewGuid(),
            MessageId: Guid.NewGuid(),
            ResidentId: residentId,
            ReceivedAt: DateTimeOffset.UtcNow);

        var result = await agent.ExecuteAsync(input, ctx);

        result.IsSuccess.Should().BeTrue("ec-0017: agent should succeed");

        var output = result.Value!;
        PrintResult("ec-0017", output.SimilarTickets.Count, minCount: 1,
            output.ConfidenceLevel, ConfidenceLevel.High);

        output.SimilarTickets.Count.Should().BeGreaterThanOrEqualTo(1,
            "ec-0017: plumbing input → at least one plumbing ticket in top-K");
        output.ConfidenceLevel.Should().Be(ConfidenceLevel.High,
            "ec-0017: plumbing texts share significant character patterns → High");
    }

    // ── ec-0018: Elevator input — Category=EnricherIntegration ───────────────────
    // Similar elevator input should surface ≥1 elevator ticket at High confidence.

    [Fact, Trait("Category", "EnricherIntegration")]
    public async Task ec0018_ElevatorInput_FindsSimilarElevatorTickets()
    {
        if (_fixture.ModelPath == null) return;

        using var onnx = new OnnxEmbeddingService(_fixture.ModelPath);
        await using var db = _fixture.CreateDbContext();
        var agent = new EnricherAgent(onnx, new TicketRepository(db), NullLogger<EnricherAgent>.Instance);

        var residentId = Guid.NewGuid();
        var input = new EnricherInput(
            TicketId: Guid.NewGuid(),
            ResidentId: residentId,
            RawText: "Asansör bozuk, katlara çıkamıyoruz.",
            ClassifiedCategory: TicketCategory.Elevator);

        var ctx = new AgentContext(
            CorrelationId: Guid.NewGuid(),
            MessageId: Guid.NewGuid(),
            ResidentId: residentId,
            ReceivedAt: DateTimeOffset.UtcNow);

        var result = await agent.ExecuteAsync(input, ctx);

        result.IsSuccess.Should().BeTrue("ec-0018: agent should succeed");

        var output = result.Value!;
        PrintResult("ec-0018", output.SimilarTickets.Count, minCount: 1,
            output.ConfidenceLevel, ConfidenceLevel.High);

        output.SimilarTickets.Count.Should().BeGreaterThanOrEqualTo(1,
            "ec-0018: elevator input → at least one elevator ticket in top-K");
        output.ConfidenceLevel.Should().Be(ConfidenceLevel.High,
            "ec-0018: elevator texts share significant character patterns → High");
    }

    // ── ec-0020: Unrelated input — Category=EnricherIntegration ─────────────────
    // Noise complaint: minimal character overlap with plumbing/elevator seed.
    // Expected: Low confidence. Note: character-level tokenizer — adjust if Medium.

    [Fact, Trait("Category", "EnricherIntegration")]
    public async Task ec0020_NoiseInput_LowSimilarity_LowConfidence()
    {
        if (_fixture.ModelPath == null) return;

        using var onnx = new OnnxEmbeddingService(_fixture.ModelPath);
        await using var db = _fixture.CreateDbContext();
        var agent = new EnricherAgent(onnx, new TicketRepository(db), NullLogger<EnricherAgent>.Instance);

        var residentId = Guid.NewGuid();
        var input = new EnricherInput(
            TicketId: Guid.NewGuid(),
            ResidentId: residentId,
            RawText: "Komşu gürültü yapıyor, gece uyuyamıyorum.",
            ClassifiedCategory: TicketCategory.Noise);

        var ctx = new AgentContext(
            CorrelationId: Guid.NewGuid(),
            MessageId: Guid.NewGuid(),
            ResidentId: residentId,
            ReceivedAt: DateTimeOffset.UtcNow);

        var result = await agent.ExecuteAsync(input, ctx);

        result.IsSuccess.Should().BeTrue("ec-0020: agent should succeed even with low similarity");

        var output = result.Value!;
        PrintResult("ec-0020", output.SimilarTickets.Count, minCount: 0,
            output.ConfidenceLevel, ConfidenceLevel.Low);

        // Character-level tokenizer may score differently — High is the only excluded outcome.
        // Exact Low assertion; update to Medium if consistently failing.
        output.ConfidenceLevel.Should().Be(ConfidenceLevel.Low,
            "ec-0020: noise text should not be highly similar to plumbing/elevator seed");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static void PrintResult(
        string caseId,
        int actualCount, int minCount,
        ConfidenceLevel actualConf, ConfidenceLevel expectedConf)
    {
        var countPass = actualCount >= minCount;
        var confPass  = actualConf == expectedConf;
        Console.WriteLine(
            $"[{caseId}] SimilarTickets: {actualCount} >= {minCount} {(countPass ? "✓" : "✗")} " +
            $"| Confidence: {actualConf} == {expectedConf} {(confPass ? "✓" : "✗")}");
    }

    // ── Stubs ─────────────────────────────────────────────────────────────────────

    private sealed class EmptyTicketRepository : ITicketRepository
    {
        public Task AddAsync(Ticket ticket, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task AddRangeAsync(IEnumerable<Ticket> tickets, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<SimilarTicket>> FindSimilarAsync(
            float[] vector, Guid excludeTicketId, int topK = 5, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SimilarTicket>>([]);

        public Task<Ticket?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<Ticket?>(null);

        public Task<(IReadOnlyList<Ticket> Items, int TotalCount)> GetPagedAsync(
            TicketStatus? status, TicketCategory? category, bool? isEmergency,
            Guid? residentId, int page, int pageSize,
            DateTime? fromUtc = null, DateTime? toUtc = null, CancellationToken ct = default)
            => Task.FromResult<(IReadOnlyList<Ticket>, int)>(([], 0));
    }
}
