## Day 8 — 2026-05-21 (Perşembe)

### Bugün yapılanlar
- TelegramAdapter implement edildi (ffd5148):
  Telegram.Bot 22.10.0.1, polling, Keyed Services,
  UTC guarantee, user-secrets.
- Classifier Prompt v1 optimize edildi (d8a57af + 84cd096):
  taxonomy.v4 aligned: 14 category, confidence,
  secondary_issues, ambiguity. Eval öncesi doğrulandı.
- Eval Baseline PASS (092d168):
  Category: 93.3% / Recall: 100% / Precision: 75%.
  Phase 1 DoD "%80+ baseline" kapatıldı.
- Clarification Flow Option C (33060ae + ace8ae0):
  TriageResult.AmbiguityReasons, Türkçe template,
  ChannelConsumerJob skeleton. Orchestrator channel
  bilmez, caller yanıtlar.
- OnnxEmbeddingService (7e6e3d7): IEmbeddingService DIP,
  Strategy A (download-models.sh, sha256 checksum),
  Singleton lifecycle. Day 9 Enricher altyapısı hazır.
- ADR-0007 (Clarification Flow) + ADR-0008 (ONNX)
  commit edildi.
- **PHASE 1 DOD KAPANDI** — tüm kriterler karşılandı.
- **Hat aktif** — Meta Business testler devam ediyor.
  Verification timer bugün başladı.

### Eval Baseline — Classifier v1 (Haiku 4.5)

| Metric             | Sonuç   | Threshold | Durum |
|--------------------|---------|-----------|-------|
| Category Accuracy  | 14/15   | ≥ %80     | ✓ %93,3 |
| Emergency Recall   | 3/3     | ≥ %95     | ✓ %100,0 |
| Emergency Precision| 3/4     | ≥ %70     | ✓ %75,0 |

**Overall: PASS**

**Başarısız case:** `[common_area_normal_01]` → expected: CommonArea, got: Security  
Fixture: "Bodrum kattaki ortak depo kapısının kilidi bozulmuş, kapı açık kalıyor." Kilit + açık kapı → model Security'ye çekmiş. Boundary: Security = erişim kontrolü / hırsızlık / vandalizm; CommonArea = ortak tesis bakımı / hasar. Day 16 prompt v2'de bu ayrım few-shot örnek ile netleştirilecek.

**False positive emergency (precision 3/4):** `fp_heat_weather_01` — "Bu hafta hava yangın gibi sıcak, klimayı açayım dedim çalışmıyor." + `emergency_suspected=true` + matched_phrases=["yanıyor"]. "Yangın gibi" deyimi + phrase match birlikte modeli emergency'ye çekmiş. Loom Q3: "context-confusing idiom + soft signal = false positive." Day 16 prompt v2'ye negative example olarak girilecek: `is_emergency: false` + explicit reasoning idiom != actual fire.

### Beklemediğim problem / sürpriz
- Classifier Prompt v1 taxonomy.v4 ile uyumsuzdu
  (6/14 category, confidence yok). Architect sıralama
  hatasını yakaladı: eval öncesi prompt doğrulanmadıysa
  baseline geçersiz. Düzeltildi, önce prompt commit,
  sonra eval.
  **Loom Q4 hammaddesi:** "Sadece kodu değil, test
  düzenini de doğrulamanın önemi."
- Precision %75 — hedef ≥70%, geçti ama dar.
  Kabul edilebilir: emergency senaryosunda false
  negative (Recall: 100%) false positive'den tehlikeli.
  Framing: "Recall'ı maksimize ettik, precision
  trade-off bilinçli."
- Telegram.Bot v22 breaking change: tüm API metodları
  extension method'a taşındı, `Async` suffix kaldırıldı
  (`GetUpdatesAsync` → `GetUpdates`, `SendTextMessageAsync`
  → `SendMessage`). Build-time catch — runtime surprise yok.

### Aldığım karar + sebep
- **Karar:** Clarification Option C — orchestrator
  channel bilmez, caller reply gönderir.
  - **Neden:** Separation of concerns. Orchestrator
    business logic, channel routing caller sorumluluğu.
  - **Loom Q5:** "Mesajı okuyan mesajı yanıtlar"
    prensibi — clean layering.

- **Karar:** ONNX Strategy A — models/ .gitignore,
  download script, Fly.io Dockerfile RUN.
  - **Neden:** Model dosyaları repo'ya girmez (büyük,
    binary, CI/CD'yi şişirir). Script + checksum
    reproducible download garanti eder.

- **Karar:** TelegramAdapter Keyed Services şimdi
  (ChannelType.Telegram key), unkeyed değil.
  - **Neden:** WhatsApp adapter (Day 10) gelince aynı
    interface'e ikinci registration → DI resolution
    belirsiz. Önceden keyed yazmak Day 10'daki zorunlu
    refactor'u önlüyor (Architect kararı).

### Keşke önceden bilseymişim
- "Eval baseline almadan önce prompt'u doğrula"
  adımı Phase 1 DoD listesinde explicit yoktu.
  Day 9+ için: Prompt Engineer review, taxonomy
  alignment check, eval sıralaması — bunlar Phase 2
  agent eval'larında da geçerli. Checklist'e eklenmeli.
- Telegram.Bot major version atlamasında API surface
  dönüşümünü önceden inceleseyim compile → run →
  discover yerine proposal aşamasında nitelendirirdim.
  Library version araştırması NuGet XML docs kontrolünü
  içermeli.

### Pro Usage Note
- İlk gerçek Anthropic API çağrıları: eval run 15 case
  × ~2000 token = ~30K token. $0.03 civarı (Haiku).
  Budget sayacı başladı. $30 alert aktif ✅.

### Yarın (Day 9, 22 May Cuma)
- EnricherAgent design proposal → Architect → implement
- RouterAgent + emergency fast-path
- ADR 0007-0008 repo'ya alındı (6c06307) ✅
- QA Hunter + Security & Compliance agent kurulumu
- Meta verification takip (Day 1 bekleme)
