# ADR-0004: Neon Postgres over Self-Hosted Docker

## Status
Accepted

## Date
2026-05-17

## Context
Proje PostgreSQL 16 + pgvector kullanıyor (kilitli). Day 0'da
varsayılan yaklaşım local Docker container üzerinde self-hosted
Postgres'ti. Day 3'te (17 Mayıs 2026) Build implementasyon
sırasında bu yaklaşım bloke yarattı.

Donanım constraint'i: MacBook Air 2017, 8GB RAM 1600MHz DDR3,
Intel Core i5 1.8GHz. Bu makine üzerinde aynı anda .NET 8
runtime, EF Core migration'ları, ONNX Runtime ve Postgres
Docker container çalıştırılmaya çalışıldı.

Gözlemlenen problem: Postgres Docker container memory
pressure altında stabil çalışmadı, dev loop bloke oldu.
Build akut karar protokolü kapsamında (henüz formalize
edilmemişti, Day 4'te ADR-0001 ile kuruldu) Neon free
tier'a geçiş kararı verdi.

Ayrıca: Fly.io deployment hedefi zaten Neon ile uyumlu —
managed Postgres, Fly.io'nun kendi Postgres offering'ından
daha olgun ve pgvector desteği hazır.

## Decision
Self-hosted Docker Postgres kaldırıldı. Neon free tier
managed Postgres kullanılmaya başlandı; hem local dev hem
production (Fly.io) bu bağlantı üzerinden çalışıyor.

## Consequences

### Positive
- Local dev makinesinde Docker Postgres memory pressure'ı
  ortadan kalktı, dev loop stabil hale geldi
- Neon free tier: 0.5 GB storage, shared compute —
  proje ölçeği için yeterli
- pgvector extension Neon'da hazır, ek kurulum gerekmedi
- Dev ve production aynı Postgres instance'ı kullanıyor
  (environment parity): migration davranışı tutarlı
- Fly.io deployment'ta managed Postgres kurulum/bakım yükü
  yok
- Bağlantı string değişikliği dışında kod değişikliği
  olmadı — EF Core + Npgsql abstraction'ı tuttu

### Negative / Trade-offs
- Network latency: local Docker'a göre Neon'a bağlantı
  network round-trip ekliyor (dev sırasında gözlemlenebilir,
  production'da Fly.io — Neon aynı region'da olunca
  minimize ediliyor)
- Neon free tier limitleri: compute auto-suspend (5 dakika
  inaktivite), bağlantı sayısı sınırı — dev'de
  dikkat gerektiriyor
- İnternet bağlantısı kesilirse local dev çalışmaz —
  self-hosted'da bu sorun yoktu
- Veri Türkiye'de değil (KVKK) — production'da dikkat:
  apartman sakinlerinin verisi Neon'da saklanıyor, Neon'un
  region seçimi ve veri işleme sözleşmesi gözden geçirilmeli

### Neutral
- Bu karar acute protokol kapsamında alındı (Day 3) —
  protokol henüz formalize edilmemişti, retroactive ADR
  bu kararı kayıt altına alıyor
- Fly.io'nun kendi Postgres offering'ı (Fly Postgres)
  değerlendirilmedi — managed değil, operasyon yükü aynı
  sorunu yaratırdı

## Alternatives Considered

### Alternative A: Self-hosted Docker Postgres (devam)
Mevcut yaklaşım. Memory pressure altında container
restart, swap, resource limit ayarları ile stabilize
edilmeye çalışılabilirdi.

Rejected because: 8GB RAM constraint'i kalıcı — Docker
Postgres + .NET + ONNX Runtime aynı anda stabil çalışmıyor.
Swap ile geçici çözüm dev loop'u yavaşlatır, problemi
çözmez.

### Alternative B: Fly.io Postgres (erken deploy)
Production ortamını local dev olarak kullanmak.

Rejected because: Dev iterasyonu production'a bağlamak
risk yaratır; migration hataları, test verisi kirliliği
production'ı etkiler. Neon dev/prod ayrımını koruyarak
aynı managed avantajı sağlıyor.

### Alternative C: SQLite (dev only)
Local dev'de SQLite, production'da Postgres.

Rejected because: pgvector SQLite'ta mevcut değil;
EnricherAgent'ın vector search'ü Day 8'de geliyor,
bu tarihten önce pgvector çalışır olmalı. Environment
parity kaybı kabul edilemez.

## References
- apartment_triage_roadmap.md §2 (Final Tech Stack —
  PostgreSQL 16 + pgvector)
- apartment_triage_roadmap.md §4 (Risk Register)
- claude_project_primer.md §7 (Donanım bağlamı: MacBook
  Air 2017, 8GB RAM)
- ADR-0003 (WhatsApp Cloud API — Fly.io deployment bağlamı)
- CLAUDE.md — Acute Decisions Under Pressure (Day 4'te
  formalize edildi, bu karar canonical örnek)
