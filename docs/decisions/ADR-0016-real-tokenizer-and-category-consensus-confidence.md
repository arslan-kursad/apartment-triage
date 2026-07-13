# ADR-0016 — Real XLM-R Tokenizer (BlingFire) + Category-Consensus Enricher Confidence

**Status:** Accepted
**Date:** 2026-07-13
**Decider:** Architect
**Supersedes:** ADR-0015
**Resolves:** issue #5

---

## Bağlam

ADR-0015, `EnricherAgent`'ın benzerlik-güven sinyalinin ayırt edici olmadığını belgelemiş
ve iki kök sebep tespit etmişti: (1) `OnnxEmbeddingService.Tokenize()` gerçek bir XLM-RoBERTa
tokenizer değil, karakter-seviyeli bir placeholder'dı; (2) `ComputeConfidence` mutlak top-1
cosine eşiğine (`>0.85 High`) dayanıyordu ve bu eşik hiçbir kalibrasyon kaydı olmadan
seçilmişti. `ec0020` (alakasız gürültü şikayeti → Low beklenir) bu yüzden skip edilmişti.

Bu ADR her iki kök sebebi de çözer.

---

## Karar 1 — Gerçek tokenizer: BlingFire

### Spike (ölç, varsayma)

Python `transformers` (`intfloat/multilingual-e5-small`, `XLMRobertaTokenizer`) ile 5 test
string için ground-truth token ID'leri üretildi (oracle). İki .NET adayı bu oracle'a karşı
test edildi:

| Aday | Sonuç |
|------|-------|
| `Microsoft.ML.Tokenizers` 1.0.2 | **Elendi.** XLM-R'ın modeli **Unigram** SentencePiece (proto'dan doğrulandı: `model_type=1`, 250000 piece). ML.Tokenizers yalnızca **BPE** SentencePiece destekliyor → modeli açıkça reddetti (`"The model type is not Bpe"`). .NET'te XLM-R unigram için pure-managed yol yok. |
| **BlingFire** (`BlingFireNuget` 0.1.8) | **Byte-exact.** `xlm_roberta_base.bin` subword ID'lerinin tamamını oracle ile birebir üretti; tek fark `<s>`(0)/`</s>`(2) sarmalını eklememesiydi. Sarmayı biz ekleyince **5/5 exact match**. |

### Karar

`BlingFireNuget` eklendi (yeni dependency — Architect onaylı). `OnnxEmbeddingService.Tokenize()`
artık BlingFire'ın `xlm_roberta_base.bin` modelini kullanıyor, sonucu `<s>`(0)…`</s>`(2) ile
sarıyor, `MaxTokens=128`'e truncate ediyor.

### Deploy / paketleme

BlingFire kaynak + native lib + 17 model'i `contentFiles` ile shipliyor (default output'a
~99 MB kopyalıyor). `ExcludeAssets=contentFiles` + seçici re-include ile output ~14 MB'a
indirildi (yalnızca wrapper `.cs` + `xlm_roberta_base.bin` + native lib'ler). Model paketin
içinde geldiği için ONNX modeli gibi ayrı indirme gerektirmiyor.

### Native platform kapsamı (dürüst uyarı)

| Ortam | ONNX 1.26.0 | BlingFire 0.1.8 |
|---|---|---|
| **Linux x64** (CI + Fly.io prod) | ✓ | ✓ |
| macOS x64 | ✗ (yalnız osx-arm64) | ✓ |
| macOS arm64 (Apple Silicon) | ✓ | ✗ (dylib x64-only) |

ONNX yalnız `osx-arm64`, BlingFire yalnız `osx-x64` native shipliyor → tek bir macOS
process'inde ikisi birden yüklenemez. **Asıl çalışma ortamı (CI + Fly.io) Linux x64 ve orada
ikisi de sorunsuz.** Etkilenen tek şey lokal macOS'ta embedding geliştirme; çözüm Docker ya da
mevcut `NoopEmbeddingService` dev fallback'i.

---

## Karar 2 — Category-consensus confidence

### Ölçüm: tokenizer fix öncesi vs sonrası

Gerçek pgvector path'i üzerinden (3 sorgu × 7 seed), top-1 cosine'ler:

| Sorgu | ESKİ (placeholder) top | YENİ (BlingFire) top |
|---|---|---|
| plumbing_query | Plumbing 0.977 | Plumbing **0.923** (top-4 hepsi Plumbing) |
| elevator_query | Elevator 0.983 | Elevator **0.923, 0.916** (sonra 0.05 boşluk) |
| noise_query (alakasız) | Plumbing 0.969 | Plumbing **0.892** |

Eski dağılım `[0.945, 0.983]` bandında tamamen iç içeydi. Yeni dağılım `[0.817, 0.923]`'e
genişledi ve **her same-category top (0.923/0.923/0.916) artık noise'un top'unu (0.892) geçiyor.**

### Neden mutlak eşik değil

İlerlemeye rağmen noise-top (0.892) ile same-category-top (0.916-0.923) arasındaki fark ince.
High eşiğini 0.90'a çekmek 3 probe'a overfit olurdu — ADR-0015'in reddettiği anti-pattern'in
sadece daha güzel sayılarla tekrarı. Ayırt edici olan mutlak cosine değil: **en yakın geçmiş
ticket'ların, Classifier'ın zaten atadığı kategoriyi doğrulayıp doğrulamadığı.**

### Yeni kural

`ComputeConfidence(similar, classifiedCategory)`:

- **High** — top-1 komşunun kategorisi classified kategoriyle aynı (en güçlü doğrulama).
- **Medium** — classified kategori top-K'da var ama rank-1'de değil (kısmi doğrulama).
- **Low** — hiçbir komşu classified kategoriyi paylaşmıyor; ya da hiç komşu yok.

Kategori-imbalance'a dayanıklı: `ec0018`'de yalnızca 2 elevator seed var (asla plurality
olamaz), ama rank-1 elevator olduğu için doğru şekilde High. Plurality-tabanlı bir kural
`ec0018`'i kırardı.

### Eval sonuçları (assertion'lar zayıflatılmadan)

| Case | Girdi | Sonuç | Assertion |
|---|---|---|---|
| ec0017 | Plumbing, top-1 Plumbing | High | değişmedi, geçiyor |
| ec0018 | Elevator, top-1 Elevator | High | değişmedi, geçiyor |
| ec0020 | Noise, top-K'da Noise yok | Low | **orijinal `Be(Low)`**, un-skip, geçiyor |
| ec0019 | Plumbing, boş DB | Low | değişmedi, geçiyor |

`ec0020` skip'i, **orijinal assertion'ı korunarak** kaldırıldı — test hiç yanlış değildi,
sistem yanlıştı. "Testi zayıflatma" değil, "sistemi düzeltme".

---

## Bilinen sınırlama (dürüst kayıt)

Saf category-consensus mutlak benzerliği hiç dikkate almıyor. Tek-kategorili bir DB'de (örn.
yalnızca Plumbing ticket'ları) her Plumbing-classified şikayet, metin gerçekten benzemese bile
top-1 Plumbing olacağı için High alır. Gerçek binada ticket'lar birkaç kategoride yoğunlaştığı
için bu bir risk. Bir sonraki iyileştirme: gerçek out-of-distribution benzerlik ölçümüyle
(elimizde henüz yok) kalibre edilmiş bir mutlak "garbage-reject" floor eklemek. Ölçülmemiş bir
sabit eklemek yerine sınırlama açıkça kaydedildi (aynı disiplin: sihirli sabit = kalibrasyon
kaydı).

---

## Reddedilen alternatifler

- **Provisional eşik (High≥0.90) + ec0020'yi `NotBe(High)`'a hizalama:** 3 probe'a overfit;
  ADR-0015'in zaten reddettiği yol.
- **Plurality-of-top-K kategori oyu:** `ec0018`'i kırar (2 elevator seed asla plurality olamaz).
- **`Microsoft.ML.Tokenizers`:** XLM-R unigram modelini yükleyemiyor (yalnız BPE).
- **Mutlak cosine floor'u şimdi eklemek:** ölçülmüş verimiz yok; ölçülmemiş sabit ADR-0015'in
  dersini tekrar ihlal ederdi.

---

## Referanslar

- ADR-0015 (bu ADR onu supersede ediyor; ec0020'nin skip gerekçesi + before ölçümü)
- ADR-0008 (ONNX embedding altyapısı; locked stack: multilingual-e5-small)
- issue #5 (tokenizer takip issue'su — bu ADR ile resolve)
- `src/ApartmentTriage.Infrastructure/Embeddings/OnnxEmbeddingService.cs`
- `src/ApartmentTriage.Application/Agents/Enricher/EnricherAgent.cs` (ComputeConfidence)
- `src/ApartmentTriage.Infrastructure/ApartmentTriage.Infrastructure.csproj` (BlingFire include)
- `tests/ApartmentTriage.Tests/Eval/EnricherEvalTests.cs` (ec0017/18/19/20)
