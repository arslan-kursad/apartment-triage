## Day 8 — 2026-05-21

### Bugün yapılanlar
- TelegramAdapter implement edildi (Telegram.Bot 22.10.0.1, polling, ChannelType.Telegram keyed)
- Classifier system prompt v1.0.0 repo'ya alındı (agents/classifier/prompts/classifier.v1.md)
- Classifier eval baseline çalıştırıldı — tüm threshold'lar geçti

### Eval Baseline — Classifier v1 (Haiku 4.5)

| Metric             | Sonuç   | Threshold | Durum |
|--------------------|---------|-----------|-------|
| Category Accuracy  | 14/15   | ≥ %80     | ✓ %93,3 |
| Emergency Recall   | 3/3     | ≥ %95     | ✓ %100,0 |
| Emergency Precision| 3/4     | ≥ %70     | ✓ %75,0 |

**Overall: PASS**

**Başarısız case:** `[common_area_normal_01]` → expected: CommonArea, got: Security  
Tesiste ortak alan meselesi Security olarak sınıflandırılmış. Fixture veya prompt revizyonu Day 16 prompt v2 iterasyonuna park edildi.

**False positive emergency (precision 3/4):** 1 case gerçek emergency değilken emergency olarak işaretlendi. %75 hâlâ threshold üstünde (%70), Day 16'ya park edildi.

### Beklemediğim problem / sürpriz
- Telegram.Bot v22 breaking change: tüm API metodları extension method'a taşındı, `Async` suffix kaldırıldı (`GetUpdatesAsync` → `GetUpdates`, `SendTextMessageAsync` → `SendMessage`). Build-time catch — runtime surprise yok.

### Aldığım karar + sebep
- **Karar:** TelegramAdapter Keyed Services şimdi (ChannelType.Telegram key), unkeyed değil.  
  **Neden:** WhatsApp adapter (Day 10) gelince aynı interface'e ikinci registration → DI resolution belirsiz. Önceden keyed yazmak Day 10'daki zorunlu refactor'u önlüyor.  
  **Alternatif:** Unkeyed + Day 10'da refactor → reddedildi (Architect kararı).

- **Karar:** TelegramAdapter polling tercih, webhook değil.  
  **Neden:** Dev/fallback rolü — webhook public HTTPS URL + imza doğrulama gerektirir. Polling local'de tunnel gerektirmez.  
  **Alternatif:** Webhook (Fly.io'da mümkün) → Day 10 WhatsApp'a bırakıldı.

### Keşke önceden bilseymişim
- Telegram.Bot major version atlamasında API surface dönüşümünü önceden inceleseyim compile → run → discover yerine proposal aşamasında nitelendirirdim. Library version araştırması NuGet XML docs kontrolünü içermeli.

### Yarın
- Görev 3: Clarification flow — proposal → implement
- Görev 4: ONNX proposal (Telegram + eval + clarification tamamlanınca)
