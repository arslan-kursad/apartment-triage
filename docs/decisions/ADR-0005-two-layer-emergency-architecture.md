# ADR-0005: Two-Layer Emergency Architecture
         (Phrase Detection + LLM Confirmation)

## Status
Accepted

## Date
2026-05-15

## Context
Apartman triage sistemi bakım taleplerini sınıflandırıyor.
Sınıflandırılan talepler arasında acil durumlar (yangın,
su baskını, gaz kaçağı, elektrik arızası) bulunuyor. Bu
talepler standart pipeline'dan farklı davranış gerektiriyor:
düşük latency, yüksek recall, false negative toleransı sıfır.

Day 1'de (15 Mayıs 2026) taxonomy v3 lock kapsamında
emergency kategorisinin mimari katmanı tasarlandı. İki
uç alternatif değerlendirildi:

- **Sadece LLM:** Her mesaj ClassifierAgent'a gidiyor,
  LLM emergency kararı veriyor. Basit ama yavaş ve
  LLM hata payı taşıyor.
- **Sadece phrase detection (keyword):** Regex/keyword
  listesi ile emergency tespiti. Hızlı ama brittle —
  "yangın gibi sıcak" false positive, "biraz gaz kokusu
  var" false negative riski.

## Decision
İki katmanlı emergency mimarisi implement edildi:

**Katman 1 — Phrase Detection (fast-path):**
Bilinen emergency keyword'leri ve pattern'ları için
deterministik kontrol. Latency: <5ms. Hit olursa
Katman 2'ye geçmeden emergency flag'i set edilir,
LLM confirmation tetiklenir.

**Katman 2 — LLM Confirmation:**
Phrase detection hit'i veya belirsiz mesajlar için
ClassifierAgent LLM çağrısı yapılır. Emergency
threshold düşük tutulur (recall öncelikli). LLM
confirmation false negative'i phrase detection
catch edemediği edge case'ler için güvence katmanı.

Fast-path: Phrase detection hit → LLM confirmation
→ immediate escalation (RouterAgent bypass).

## Consequences

### Positive
- False negative riski minimize: hem keyword hem LLM
  emergency'yi kaçırmak zorunda (iki bağımsız katman)
- Latency optimize: phrase detection <5ms, çoğu
  non-emergency mesaj LLM'e gitmeden eleniyor
- Test edilebilirlik: her katman bağımsız unit test
  edilebilir — phrase list regression test'i + LLM
  eval ayrı ayrı çalışıyor
- Loom Q3 (edge case) için güçlü anlatı: "yangın
  gibi sıcak" false positive'ini nasıl handle ettiğimiz
  somut, gösterilebilir vaka
- Phrase list genişletilebilir (Türkçe dialekt varyantları,
  emoji pattern'ları) LLM katmanını etkilemeden

### Negative / Trade-offs
- İki katman = iki bakım noktası: phrase list güncel
  tutulmalı, LLM prompt'u ayrıca iterate ediliyor
- LLM confirmation her emergency için API çağrısı
  demek — maliyet sıfır değil ama emergency frekansı
  düşük, bütçe etkisi minimal
- Phrase detection false positive'i LLM'e gereksiz
  yük bindiriyor — "yangın gibi sıcak" tipi edge
  case'ler LLM confirmation'a düşüyor

### Neutral
- Phrase list Türkçe öncelikli; İngilizce ve emoji
  pattern'ları ek olarak — apartman sakinleri Türkçe
  yazıyor
- RouterAgent bypass kararı bu ADR kapsamında değil;
  router mimarisi ayrı ADR konusu olabilir (Day 9+)

## Alternatives Considered

### Alternative A: Sadece LLM classification
Her mesaj ClassifierAgent'a gidiyor, emergency kararı
LLM veriyor. Ayrı phrase detection yok.

Rejected because: LLM latency (300-800ms) emergency
fast-path için kabul edilemez; LLM hata payı (hallucination,
context window sorunu) güvenlik-kritik kategori için
tek savunma katmanı olamaz; rate limit veya API outage
durumunda emergency detection tamamen devre dışı kalır.

### Alternative B: Sadece phrase detection (keyword list)
LLM olmadan deterministik emergency tespiti.

Rejected because: Brittle — "az duman var", "biraz
yanık kokusu", emoji-only mesajlar ("🔥❓") phrase
list'i atlayabilir; dil varyasyonu ve yanlış yazım
toleransı düşük; false positive kontrolü zor.

### Alternative C: ML classifier (fine-tuned)
Emergency tespiti için fine-tuned küçük model.

Rejected because: Training data yok (apartman
bağlamında labeled emergency dataset); 21 günlük
timeline ile fine-tuning yapılamaz; ONNX Runtime
zaten embeddings için kullanılıyor, ayrı bir
classifier modeli scope dışı.

## References
- apartment_triage_roadmap.md §5 (Day 9: RouterAgent
  + emergency keyword detector)
- claude_project_primer.md §3 (Sorgulanmayacak Kararlar)
- Taxonomy v3 lock (Day 1, 15 Mayıs 2026)
- ADR-0001 (Custom Orchestrator — pipeline mimarisi bağlamı)
