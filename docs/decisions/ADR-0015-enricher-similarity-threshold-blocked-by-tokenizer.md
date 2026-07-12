# ADR-0015 — Enricher Similarity Thresholds: Calibration Blocked by Placeholder Tokenizer

**Status:** Accepted
**Date:** 2026-07-12
**Decider:** Architect
**Context:** İlk gerçek CI run'ı (Testcontainers + pgvector, taze DB) — `ec0020` fail

---

## Bağlam

CI workflow eklenirken (bkz. `.github/workflows/ci.yml`), `EnricherIntegration` trait'i
ilk kez gerçek bir Testcontainers pgvector container'ına karşı, gerçek ONNX embedding'lerle
koştu. Bu path daha önce hiç taze bir veritabanına karşı uçtan uca çalışmamıştı (fixture'da
iki ayrı bug bulundu ve düzeltildi — bkz. ilgili commit'ler — ama onlar altyapı sorunlarıydı).

Altyapı düzeldikten sonra `ec0020_NoiseInput_LowSimilarity_LowConfidence` gerçek bir
başarısızlıkla kaldı: alakasız bir gürültü şikayeti, plumbing/elevator seed'lerine karşı
`ConfidenceLevel.High` (cosine > 0.85) skorladı — beklenen `Low` değil.

### Kök sebep

`OnnxEmbeddingService.Tokenize()` gerçek bir XLM-RoBERTa/SentencePiece tokenizer değil —
kod içinde açıkça işaretlenmiş bir **placeholder**:

```csharp
// PLACEHOLDER: character-level mapping produces valid tensor shapes but
// semantically approximate embeddings.
// Replace with proper XLM-RoBERTa SentencePiece tokenizer before Day 14 deploy.
// Pending: tokenizer library Architect approval (separate flag).
```

Bu escalation hiç yapılmadı — placeholder, journal'da veya başka bir ADR'de hiç
anılmadan production'a kadar geldi (ADR-0008 model altyapısını kapsıyor ama tokenizer
kalitesini tartışmıyor).

Karakter-seviyeli "tokenization" (`ch % 50_000 + 100`), modelin embedding lookup
tablosuna eğitim sırasında hiç görmediği ID'ler gönderir. Sonuç: embedding, gerçek
semantik içeriği değil, büyük ölçüde yüzeysel karakter/n-gram örtüşmesini yansıtıyor —
ve Türkçe bakım şikayetleri (`"var"`, `"sürekli"`, ortak fiil çekimleri vb.) kategoriden
bağımsız olarak yüksek yüzeysel örtüşme paylaşıyor.

### Ölçüm

Bu ADR'a varmadan önce, gerçek pgvector-backed `FindSimilarAsync` path'i üzerinden
3 sorgu × 7 seed = 21 karşılaştırmanın tamamı ölçüldü (geçici bir diagnostic test ile,
CI'da gerçek container'a karşı — bkz. commit history, diagnostic scaffolding sonradan
kaldırıldı):

```
plumbing_query → Plumbing .9770 .9732 | Elevator .9678 .9672 | Plumbing .9648 .9618 .9591
elevator_query → Elevator .9830 .9804 | Plumbing .9801 .9724 .9719 .9712 .9710
noise_query    → Plumbing .9691 .9681 .9640 | Elevator .9572 | Plumbing .9571 .9449
```

Tüm değerler **[0.9449, 0.9830]** dar bandında — kategori-içi ve kategoriler-arası
aralıklar tamamen iç içe geçmiş. `noise_query`'nin en iyi (yanlış) eşleşmesi (0.9691),
`plumbing_query`'nin kendi doğru kategorisindeki 4. eşleşmesinden (0.9591) bile yüksek.

**Sonuç: hiçbir sabit eşik, doğru eşleşmeleri High'da tutup noise'u High'ın dışında
tutmayı aynı anda sağlayamaz.** Bu bir kalibrasyon sorunu değil — sinyalin kendisi,
mevcut tokenizer ile, ayırt edici değil.

---

## Karar

1. **`query: ` prefix eklendi** (`OnnxEmbeddingService.GetEmbeddingAsync`) — e5 ailesinin
   gerektirdiği task-instruction prefix'i, uniform olarak (query/passage ayrımı değil:
   `TriageOrchestrator`, `EnricherAgent`'ın hesapladığı aynı vektörü ticket'ın kalıcı
   temsili olarak saklıyor — yani her "geçmiş ticket" vektörü de aslında oluşturulduğu
   anda bir "query" embedding'iydi. Ayrı bir index-time passage adımı yok; bu simetrik
   bir benzerlik görevi, e5'in asimetrik retrieval konvansiyonu değil). Bu düzeltme
   ölçülebilir bir fark yaratmadı (aralık hâlâ [0.94-0.98]) — beklenen sonuç, çünkü
   placeholder tokenizer zaten prefix'in gerçek token ID'lerini üretmiyor.
2. **Eşikler (`EnricherAgent.ComputeConfidence`: >0.85 High, >0.65 Medium) değiştirilmedi.**
   Ölçülen dağılıma "uydurma" (overfit) bir eşik seçmek — örn. 3 problu veriye göre bir
   sınır çizmek — gerçek bir kalibrasyon olmaz, üç örneğe özel bir hile olurdu.
3. **`ec0020` kırmızı kalıyor, bilinçli olarak.** Test doğru şeyi yakalıyor: mevcut
   embedding sinyali alakasız içeriği ayırt edemiyor. Filtre genişletilerek veya
   assertion gevşetilerek "yeşile boyanmadı".
4. **Gerçek tokenizer (XLM-RoBERTa/SentencePiece) ayrı, büyük bir takip işi olarak
   flag'lendi** — yeni bir NuGet dependency gerektirir (örn. `Microsoft.ML.Tokenizers`
   veya benzeri), kendi ADR'ini ve Architect onayını gerektirir. Bu ADR o işi
   kapsamıyor; sadece mevcut durumu belgeliyor ve neden eşiklerin şu an
   değiştirilemeyeceğini kayda geçiriyor.

---

## Kapsam Dışı

Gerçek tokenizer implementasyonu. Yapılana kadar: `EnricherAgent`'ın confidence sinyali
üretimde güvenilir kabul edilmemeli; benzerlik sonuçları yol gösterici olarak
kullanılabilir ama otomatik yüksek-güven kararları (örn. otomatik archive/routing)
bu sinyale dayandırılmamalı.

---

## Reddedilen Alternatifler

- **Eşikleri ölçülen 3 probe'a göre yeniden ayarlamak** (örn. 0.973 gibi bir sınır):
  Reddedildi — 3 örneğe overfit olur, gerçek corpus büyüdükçe anlamsızlaşır, ve
  "sihirli sabit" sorununu çözmez, sadece yerini değiştirir.
- **`ec0020`'yi filtre dışına almak veya assertion'ı gevşetmek:** Reddedildi — test
  gerçek bir sinyal kalitesi sorununu doğru yakalıyor; testi susturmak sorunu gizler,
  çözmez.
- **`query:`/`passage:` ayrımı (asimetrik retrieval konvansiyonu):** Reddedildi —
  sistemin mimarisi (bkz. Bağlam) her ticket vektörünü kendi oluşturulma anında bir
  query olarak hesaplıyor; ayrı bir passage/index adımı yok, dolayısıyla asimetrik
  ayrım mimariyle uyuşmuyor.

---

## Referanslar

- `src/ApartmentTriage.Infrastructure/Embeddings/OnnxEmbeddingService.cs` (placeholder tokenizer yorumu)
- `src/ApartmentTriage.Application/Agents/Enricher/EnricherAgent.cs` (ComputeConfidence eşikleri)
- `src/ApartmentTriage.Application/Orchestration/TriageOrchestrator.cs` (SetEmbeddingVector — vektörün tekil/simetrik kullanımı)
- ADR-0008 (ONNX embedding altyapısı — tokenizer kalitesini kapsamıyor)
- `.github/workflows/ci.yml` (bu bulguyu ortaya çıkaran ilk gerçek CI run'ı)
- `tests/ApartmentTriage.Tests/Eval/EnricherEvalTests.cs` (`ec0020`)
