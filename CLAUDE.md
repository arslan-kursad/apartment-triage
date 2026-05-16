# Apartman Triage AI

21-günlük (14 May – 3 Jun 2026) Loom demo projesi. WhatsApp grubuna gelen apartman bakım/şikayet mesajlarını LLM agent pipeline'ı ile triage eden .NET sistem. Hedef: Kyle Richless (Zaigo.ai) için AI Integration Solutions vitrini.

## Source of truth

Bu dosya özettir. Detaylı bağlam:

- `docs/primer.md` — kişi, iletişim modu, kapanmış kararlar (sorgulanmaz)
- `docs/roadmap.md` — takvim, faz planı, risk register
- `journal/` — günlük decision journal (Loom Q4 hammaddesi)
- `config/taxonomy.v3.yaml` — LOCKED, kategorize taksonomi
- `config/emergency_phrases.v2.json` — LOCKED, acil durum tetikleyicileri

## Stack (kapanmış, sorgulanmaz)

- .NET 8 / C# + ASP.NET Core 8 Minimal API
- PostgreSQL 16 + pgvector + EF Core 8 + Npgsql
- Hangfire (Postgres-backed, Redis YOK)
- Anthropic API direct (HttpClient + System.Text.Json) — SDK YOK
- Haiku 4.5 default, Sonnet 4.6 sadece Enricher escalation
- Custom `IAgent<TIn,TOut>` orchestrator (~300 LOC) — Semantic Kernel / AutoGen / Microsoft.Extensions.AI YOK
- ONNX Runtime + multilingual-e5-small (local embedding)
- WhatsApp Cloud API direct (Meta) — Twilio BSP YOK
- Razor Pages — Blazor YOK
- xUnit + FluentAssertions + Testcontainers
- Serilog structured JSON
- Fly.io free tier hosting
- Repo private (KVKK + secrets)

## Mimari (4-katman)

```
src/ApartmentTriage.Domain/         — pure entities, enums, value objects
src/ApartmentTriage.Application/    — agent abstractions, orchestrator, use cases
src/ApartmentTriage.Infrastructure/ — EF Core, Anthropic HttpClient, channels, ONNX
src/ApartmentTriage.Web/            — Minimal API + Razor Pages + Hangfire host
tests/ApartmentTriage.Tests/        — Unit/ ve Integration/ folder separation
```

Dependency akışı: Web → Application + Infrastructure → Domain. Infrastructure, Application'ın declare ettiği interface'leri implement eder (DIP).

## İletişim ve çalışma modu

- Türkçe ağırlıklı, teknik terimler İngilizce (agent abstraction, prompt caching, escalation path vb. çevirme).
- Dürüst entelektüel partner ton. Yağcılık ve gereksiz onaylama yok; savunduğun argüman varsa arkasında dur.
- Default proceed; yalnızca kritik, geri dönüşü zor veya yüksek maliyetli kararlarda "!" işaretiyle dikkat çek ve onay iste.
- Genel sohbet: akış bozulmaz, sadece anlamı bozan majör hatalar.
- Teknik yazışma: detaylı dilbilimsel geri bildirim.
- İngilizce çıktı (Kyle email, README, agent prompt, eval rationale): preposition / tense / kültürel nüans titizliği; her İngilizce çıktıdan önce Türkçe tercümesi verilir.

## Conventions

- Branch: `main` (single-developer).
- Commit: Conventional Commits (`feat:`, `fix:`, `refactor:`, `docs:`, `chore:`, `test:`, `wip:`).
- Migration adlandırma: `YYYYMMDDHHMM_DescriptiveName`.
- Postgres column naming: `snake_case` (EFCore.NamingConventions plugin).
- Enum serialization: `JsonStringEnumConverter` + snake_case policy.
- pgvector extension: raw SQL migration (`migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;")`).
- Test before commit: `dotnet build && dotnet test` clean olmalı.

## Kapanmış kararlar (don't re-open)

.NET seçimi, custom orchestrator, HttpClient direct, Razor Pages, Hangfire, WhatsApp Cloud API direct, repo private, ONNX local, Fly.io. Detay: `docs/primer.md` §3.