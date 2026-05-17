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

## Model Routing Policy

Default: **Sonnet 4.6** (regular thinking).
Escalate only when task complexity warrants. Pro pool limited resource.

### Sonnet 4.6 (default) — workhorse
- Daily coding: scaffolding, CRUD, refactor, test yazımı
- High-volume tasks: eval case generation, batch operations
- Boilerplate: DTO/entity mapping, validation, simple endpoints
- File operations: rename, move, simple edits
- Hızlı iterasyon gereken her şey

### Sonnet 4.6 Extended Thinking — orta zorluk
Şu sinyallerden biri varsa:
- Multi-step reasoning ("orchestrator_rule pseudocode'unu C# koda çevir")
- Cross-file impact analysis (3+ dosyaya yayılan değişiklik)
- Architecture trade-off karşılaştırması (A vs B vs C)
- Edge case detection ("bu kod hangi durumlarda kırılır?")
- Debugging: stack trace + birden fazla suspect

### Opus 4.7 — decisive moments
Sadece şu durumlarda:
- Architecture decision (ADR drafting)
- Complex prompt engineering (taxonomy, classifier prompt v1)
- Gnarly multi-system debugging (Code + DB + LLM API üçlüsü karışmış)
- PM strategic conversation (Project chat default Opus)
- Loom prep (Day 18+)

### Opus 4.6 — sadece şu durumda
Opus 4.7 pool sıkışıksa fallback. Aksi halde 4.7 tercih.

### Self-check
- Günde 1-2 Opus seçimi normal
- 4+ kez Opus = task'i alt-task'lere böl, çoğunu Sonnet'e bırak
- Pro %70 dolarsa Opus kullanımı kısıtla