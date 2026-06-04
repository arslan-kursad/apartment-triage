# Apartman Triage AI

21-günlük (14 May – 3 Jun 2026) Loom demo projesi. WhatsApp grubuna gelen apartman bakım/şikayet mesajlarını LLM agent pipeline'ı ile triage eden .NET sistem. Hedef: the outreach target (the target company) için AI Integration Solutions vitrini.

## Source of truth

Bu dosya özettir. Detaylı bağlam:

- `docs/primer.md` — kişi, iletişim modu, kapanmış kararlar (sorgulanmaz)
- `docs/roadmap.md` — takvim, faz planı, risk register
- `journal/` — günlük decision journal (Loom Q4 hammaddesi)
- `config/taxonomy.v4.yaml` — LOCKED, kategorize taksonomi
- `config/emergency_phrases.v2.json` — LOCKED, acil durum tetikleyicileri

## Stack (kapanmış, sorgulanmaz)

- .NET 8 / C# + ASP.NET Core 8 Minimal API
- PostgreSQL 16 + pgvector + EF Core 8 + Npgsql
- Hangfire (Postgres-backed, Redis YOK)
- Anthropic API direct (HttpClient + System.Text.Json) — SDK YOK
- Haiku 4.5 default, Sonnet 4.6 sadece Enricher escalation
- Custom `IAgent<TIn,TOut>` orchestrator (~500 LOC) — Semantic Kernel / AutoGen / Microsoft.Extensions.AI YOK
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
- İngilizce çıktı (the target email, README, agent prompt, eval rationale): preposition / tense / kültürel nüans titizliği; her İngilizce çıktıdan önce Türkçe tercümesi verilir.

## Conventions

- Branch: `main` (single-developer).
- Commit: Conventional Commits (`feat:`, `fix:`, `refactor:`, `docs:`, `chore:`, `test:`, `wip:`).
- Migration adlandırma: `YYYYMMDDHHMM_DescriptiveName`.
- Postgres column naming: `snake_case` (EFCore.NamingConventions plugin).
- Enum serialization: `JsonStringEnumConverter` + snake_case policy.
- pgvector extension: raw SQL migration (`migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;")`).
- DateTime: Always use `DateTime` with `Kind=Utc`. Set via `DateTime.UtcNow`. EF Core relies on default Npgsql timestamptz mapping behavior. `DateTimeOffset` migration reconsidered Day 14+.
- Journal naming: `journal/YYYY-MM-DD_dayNN.md` (e.g., `journal/2026-05-21_day07.md`).
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

---

## DECISION AUTHORITY

### Can Decide (otonom hareket et, sormadan ilerle)
- Implementation detail: değişken adlandırma, internal helper method'lar, file organization
- Test yapısı: test isimlendirme, fixture organizasyonu, assertion style
- Code refactoring (public API'yi bozmadığı sürece)
- Logging detayı: structured log field'ları, log level kararları
- Local dev environment kararları (Docker compose layout, local seed data)
- Library version (aşağıdaki semver tablosuna göre)

### Library Version Authority

| Bump | Örnek | Authority |
|------|-------|-----------|
| Patch (X.Y.A → X.Y.B) | Npgsql 8.0.10 → 8.0.11 | Otonom |
| Minor — pasif | EF Core 8.0.x → 8.1.x, mevcut API kullanımıyla | Otonom |
| Minor — aktif | Aynı bump, yeni 8.1 API'larını kullanmaya başlıyorsan | Architect flag |
| Major (X.* → Y.*) | Npgsql 8.x → 9.x | Architect flag |
| Yeni dependency | Yeni NuGet package | Architect flag |

**Pasif vs aktif tanımı:** Bump sonrası `git diff` sadece `.csproj` ve `packages.lock.json` etkiliyorsa → pasif (otonom). Source kod dosyaları da değişiyorsa → aktif (flag at).

**Transitive dependency:** Bir bump altındaki dependency'leri de çekerse ve locked stack'i ihlal ederse → flag.

### Cannot Decide (Architect onayı zorunlu)
- Locked stack ihlali (primer §3)
- Yeni NuGet dependency eklenmesi
- Database schema değişikliği (migration üretimi)
- Public API surface değişikliği (IAgent, IMessageChannel, IAnthropicClient)
- Cross-agent abstraction değişikliği
- Infrastructure/hosting yön kararı
- Secret yönetimi mekanizması
- Test framework değişikliği

### Must Signal (scope dışında bir şeyle karşılaştığında)
1. Eylem yapma.
2. Architect'e flag at:

```
FLAG: [tek cümle problem tanımı]
Context: [neden bu karar gerekti, hangi task içinde çıktı]
Proposed direction: [önerin varsa — yoksa "açık" yaz]
Blocking: [bu olmadan ne ilerleyemez]
```

3. Architect cevap verene kadar paralel iş yapabilirsin — flag'lenen alana dokunma.
4. PM'e direkt gitme. Architect filter'dır.

### Anti-Pattern (dur sinyalleri)
- "Bu sadece küçük bir iyileştirme, Architect'i meşgul etmeye değmez"
- "Mantıklı olan bu, retroactive de onaylanır zaten"
- "PM/Architect'in cevabını beklersem timeline kayıyor"
- "Implementation detay, yazmaya gerek yok"

---

## ACUTE DECISIONS UNDER PRESSURE

Bu protokol **istisna mekanizmasıdır, yol değildir.**

### Akut durum nedir, ne değildir

**Akut:**
- Dev loop tamamen bloke (build/test/run çalışmıyor)
- Bir bağımlılık/servis kullanılamaz, alternatife geçilmeden ilerlenemez
- Saatler içinde karar verilmezse o günün ana hedefi düşer

**Akut değil:**
- "Daha iyi bir yol buldum" → proactive proposal, flag at
- "Bu refactor şimdi kolay, sonra zor" → scope creep, flag at
- "Implementation sırasında fark ettim ki" → önce Architect onayı almalıydın, DUR
- Performance optimization fırsatı → flag at

### Protokol (sırayla)

1. **2 satır ön bildirim** (eylemden ÖNCE):

```
ACUTE: [bloker tanımı, tek satır]
Acting on: [yapacağın değişiklik, tek satır] — retroactive review için ADR draftlayacağım.
```

2. **Minimum viable eylem** — bloker'ı aşacak en küçük değişiklik. Fırsatçılığa dönüşmesin.

3. **Eylem sonrası retroactive ADR draft** — aynı gün içinde:
   - Status: Proposed (Accepted değil — Architect onaylayacak)
   - Acute justification bölümü ekle
   - Reversibility analysis ekle

4. **Architect retroactive review** — Approved / Approved with conditions / Rejected

### Kırmızı çizgiler (akut bile olsa yapılmaz)
Locked decision (primer §3) ihlali. Stack değişimi acute justification ile bile geçmez.

### Canonical örnek: Neon vakası (Day 3)
Local Postgres Docker 8GB RAM'de stabil çalışmadı → dev loop bloke → Neon free tier'a geçildi.
Doğru karar, eksik protokol: ön bildirim PM thread'ine değil kendi session'ında yapıldı, retroactive ADR yazılmadı.
ADR-0004 bu kararı formalize ediyor.