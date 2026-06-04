# ADR-0008: ONNX Embedding Infrastructure

## Status
Accepted

## Date
2026-05-21

## Context
EnricherAgent (Day 8-9) pgvector similarity
search için text embedding'e ihtiyaç duyuyor.
Roadmap §1'de "ONNX Runtime + multilingual-e5-small
(local, free)" olarak locked.

İki karar alınması gerekti:
1. NuGet package + versiyon
2. Model file yönetim stratejisi (117MB binary)

Constraint'ler:
- Donanım: MacBook Air 2017, 8GB RAM,
  Intel Core i5 — CPU-only zorunlu
- Repo: private, binary commit edilemez
- Fly.io free tier: build sırasında model
  erişilebilir olmalı
- Sıfır yeni cloud dependency
  (local embedding, roadmap kararı)

## Decision
Microsoft.ML.OnnxRuntime 1.26.0 (CPU-only)
Infrastructure.csproj'a eklendi.

IEmbeddingService interface Application/Embeddings/
altında (DIP). OnnxEmbeddingService Infrastructure/
Embeddings/ altında, Singleton lifecycle
(InferenceSession ağır nesne, uygulama ömrü boyunca).

Model file stratejisi A: scripts/download-models.sh
+ models/ klasörü .gitignore'da. Hugging Face'den
microsoft/multilingual-e5-small ONNX export,
sha256 checksum doğrulaması.

AddEmbeddings() ayrı DI extension, modelPath
IConfiguration'dan ("Embeddings:ModelPath"),
missing config → InvalidOperationException
(Program.cs pattern ile tutarlı).

## Consequences

### Positive
- Sıfır cloud/API bağımlılığı: embedding local,
  offline çalışır, maliyet yok
- Repo clean: 117MB binary commit edilmedi
- sha256 checksum: model integrity CI'da güvenli
- Singleton InferenceSession: yükleme maliyeti
  bir kez, sonrası hızlı inference
- IEmbeddingService mock: test'lerde ONNX
  dependency gerekmez
- AddEmbeddings ayrı: test'lerde sadece
  mock embedding register edilir, ONNX yüklenmez

### Negative / Trade-offs
- download-models.sh: dev setup adımı eklenmiş.
  Yeni developer: script çalıştırmazsa
  InvalidOperationException (modelPath missing).
  README'ye eklenecek (Day 13 deploy prep).
- Fly.io Dockerfile: RUN apt-get + download adımı.
  Build süresi ~2-3 dakika artacak (117MB).
- multilingual-e5-small 384 boyut:
  Türkçe için yeterli ama domain-specific
  fine-tuning yok. Day 16+ değerlendirilebilir.

### Neutral
- Microsoft.ML.OnnxRuntime transitive:
  OnnxRuntime.Managed + runtime natives.
  Locked stack ihlali yok.
- Dimensions: 384 (IEmbeddingService.Dimensions
  property olarak expose edildi).

## Alternatives Considered

### Alternative B: Git-LFS
Model dosyasını Git-LFS ile repo'da izle.

Rejected because: LFS storage gerekir,
Fly.io'da LFS pull setup karmaşık, avantajı
Strategy A'ya göre yok.

### Alternative C: Fly.io build'de inline download
Dockerfile içinde wget ile direkt download.

Rejected because: Strategy A ile özdeş sonuç,
ama script ayrı olduğunda local dev'de de
aynı script çalışır (test edilebilir, tutarlı).

### Alternative: OpenAI/Anthropic embedding API
Bulut embedding API (text-embedding-3-small vb.)

Rejected because: Roadmap §1'de locked:
"ONNX Runtime + multilingual-e5-small (local, free)".
Bu karar Day 0'da verildi, yeniden açılmaz.

## References
- apartment_triage_roadmap.md §1
  (Embeddings: ONNX Runtime + multilingual-e5-small)
- ADR-0004 (Neon Postgres — donanım constraint bağlamı)
- scripts/download-models.sh
- src/ApartmentTriage.Infrastructure/Embeddings/OnnxEmbeddingService.cs
- journal/day-08.md
