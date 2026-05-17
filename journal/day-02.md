## Day 2 — 16 May 2026 (Cumartesi)

### Bugün yapılanlar
- Claude Code (terminal CLI) kararı verildi ve setup tamamlandı.
- .NET 8 SDK kuruldu (8.0.421, Microsoft resmi install script — Homebrew macOS 12 
  Tier 3 uyumsuzluğu nedeniyle).
- 5 proje solution oluşturuldu: Domain / Application / Infrastructure / Web / Tests 
  (clean architecture layering).
- NuGet paketleri eklendi: EF Core 8.0.10, Npgsql/pgvector, 
  EFCore.NamingConventions, Hangfire (PostgreSQL-backed), Serilog, xUnit, 
  FluentAssertions, Testcontainers.
- ApartmentTriageDbContext + AddInfrastructure(connStr) DI extension yazıldı.
- Connection string User Secrets'a alındı (repo dışı, KVKK uyumlu).
- dotnet-ef 8.0.10 local tool olarak kuruldu.
- InitialSchema EF migration oluşturuldu (CREATE EXTENSION IF NOT EXISTS vector;).
- dotnet build → 0 Warning, 0 Error.
- docker-compose.yml hazırlandı: pgvector:pg16 image, named volume, healthcheck.
- Docker CLI + Colima kuruldu (Docker Desktop yerine hafif alternatif — 8 GB RAM 
  MacBook Air 2017 için kritik karar).

### Beklemediğim problem / sürpriz
- Homebrew dotnet formula macOS 12 (Monterey) Tier 3 olduğu için kurulmadı. 
  Microsoft resmi install script ile çözüldü. 15-20 dakika kayıp.
- Docker Desktop'un MacBook Air 2017'de (8 GB RAM) RAM sıkışması yaratma riski. 
  Colima tercih edildi ama Docker runtime henüz tam ayakta değil — yarın sabah 
  10 dakikalık manuel adım (docker compose up -d + ef database update).

### Aldığım karar + sebep
- **Karar:** Claude Code'a geçiş (Build thread'i tavsiyesi, PM onayı).
  - **Neden:** 13 günlük build-heavy sprint'te manuel paste döngüsü amortize 
    edilemez. Setup tek seferlik 30-60 dk, kazanç günlük 30-60 dk net tasarruf. 
    13 günde ~6-13 saat buffer kazanımı.
  - **Alternatif:** Chat mode'da kalıp 21 gün kopyala-yapıştır. Reddedildi — 
    encoding/path/eksik satır hataları sürekli birikirdi.
  - **Trade-off:** Token tüketim hızlandı, Pro limit riski erkenleşti. Day 4 
    akşamı Pro retro (Day 7 yerine).
  - **Loom Q2 angle:** "Kendi build sürecimi de bir agent'a delege etmek bir 
    mimari karardı" — AI Integration Solutions kariyer yönüne tematik uyum.

- **Karar:** Source of truth = repo. Project Files (chat) repo'dan mirror.
  - **Neden:** İki paralel baseline (Project Files + CLAUDE.md) silent drift 
    riski. Tek authoritative source: repo. Project Files revizyon turlarında 
    sync edilir.

- **Karar:** Day 2-3 tek "Foundation" bloğu olarak yumuşatıldı.
  - **Neden:** Claude Code setup Day 2'den 30-60 dk aldı. Day 2 deliverable'ların 
    tamamını bugüne sıkıştırmak gereksiz baskı. Hard checkpoint Day 3 akşamı.

### Keşke önceden bilseymişim
- macOS 12 Tier 3 uyumsuzluğu Day 0 risk register'ında yoktu. Genel risk: 
  "eski donanım + yeni SDK = sürpriz uyumsuzluk." Day 7 retro'da risk register'a 
  "development environment compatibility" satırı eklenmeli.
- Claude Code'un token tüketim hızı henüz ölçülmedi — ilk gerçek data yarın. 
  Pro headroom'u bilinçli izlemeye başla.

### Pro Usage Note
- Day 2 usage: [yarın sabah /status ile kontrol et, buraya yaz]

### Yarın (Day 3, 17 May Pazar)
- 09:00 — Docker blocker çöz (docker compose up -d + ef database update, 10 dk)
- 09:15 — Claude Code'da devam: Ticket / Resident / Message entity'leri
- Taxonomy v3 enum'ları koda taşı (14 kategori, priority, causal_relation)
- İkinci EF migration
- IMessageChannel + MockChannel abstraction
- (Vakit kalırsa) AgentBase<TIn,TOut> skeleton
- Akşam — PM thread'ine Day 3 hard checkpoint