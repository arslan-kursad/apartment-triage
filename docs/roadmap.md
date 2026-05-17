# Apartman Triage AI — 21-Day Roadmap

**Hedef:** the outreach target (the target company) için 10 dakikalık Loom video — gerçek, canlıya alınmış, .NET tabanlı agentic apartman yönetimi triage sistemi.

**Bugün:** 14 Mayıs 2026 Çarşamba (Day 0)
**Teslim:** 3 Haziran 2026 Çarşamba (Day 20)
**Stack:** ASP.NET Core 8 + Anthropic API + PostgreSQL/pgvector + WhatsApp Cloud API

---

## 1. Stratejik Kararlar (Kilitli)

| Karar | Sonuç |
|---|---|
| Proje | Maintenance Request Triage (Katman 1 vertical slice) |
| Stack | C#/.NET — Python değil |
| Framework | Custom orchestrator (Semantic Kernel / LangGraph reddedildi) |
| Input kanalı | WhatsApp Business Cloud API (primary), Telegram (dev/fallback) |
| Deployment | Babanın binası (gerçek WhatsApp grubu) |
| LLM strategy | Haiku-first ($1/$5), Sonnet escalation ($3/$15) |
| Embedding | ONNX Runtime + multilingual-e5-small (local, free) |
| Hosting | Fly.io free tier |
| Prompt storage | Markdown + git + manifest.yaml |

---

## 2. Final Tech Stack

```
Web Layer:           ASP.NET Core 8 Minimal API
Background Jobs:     Hangfire (Postgres-backed, no Redis)
Database:            PostgreSQL 16 + pgvector extension
ORM:                 EF Core 8 + Npgsql.EntityFrameworkCore.PostgreSQL
LLM Client:          HttpClient + System.Text.Json (direct, no SDK)
Agent Framework:     Custom IAgent<TIn, TOut> (~300 LOC)
Embeddings:          Microsoft.ML.OnnxRuntime + multilingual-e5-small
Prompt Storage:      Markdown + YamlDotNet for manifest
Dashboard:           Razor Pages (no Blazor)
WhatsApp:            Meta Cloud API direct (no Twilio BSP)
Logging:             Serilog (JSON output, structured)
Hosting:             Fly.io (.NET supported, free tier)
Testing:             xUnit + FluentAssertions + Testcontainers
```

---

## 3. Communication Checklist

| Action | Deadline | Status |
|---|---|---|
| Holding email to the target | Day 0 (14 May) | ⬜ |
| Meta Business Manager verification paperwork | Day 1 (15 May) | ⬜ |
| Babanın bilgilendirilmesi (stakeholder brief) | Day 12 (26 May) | ⬜ |
| KVKK disclosure mesajı (apartman grubu) | Day 13 (27 May) | ⬜ |
| WhatsApp template approval submission | Day 8 (22 May) | ⬜ |
| Loom pre-screen (arkadaş geri bildirimi) | Day 20 (2 June) | ⬜ |
| Final Loom + email to outreach@example.com | Day 20-21 (3 June) | ⬜ |

---

## 4. Risk Register

| Risk | Olasılık | Etki | Mitigation |
|---|---|---|---|
| Meta verification 7+ gün sürmesi | Orta | Yüksek | Telegram fallback hazır, adapter pattern bu nedenle var |
| Babanın grubunda mesaj akışı yetersiz | Düşük | Yüksek | Kendi sentetik test mesajları + father coordination |
| Pro plan Claude limitleri yetersiz | Orta | Orta | Max 5x'e geçici upgrade ($100, recoverable) |
| Edge case yokluğu (sistem fazla "smooth" çalışır) | Orta | Yüksek | Bilinçli stress testing — multi-issue, emoji-only, voice messages |
| API budget aşımı ($75 sınırı) | Düşük | Düşük | Aggressive caching dev sırasında, Anthropic dashboard alert $50'de |
| Loom kayıt teknik sorunu | Düşük | Yüksek | Day 20'de prova kayıt zorunlu |

---

## 5. Day-by-Day Roadmap

### Phase 1: Foundation (Days 1-7) — Mock Channel End-to-End

**Hedef:** Mock channel üzerinden gerçek LLM ile çalışan classifier pipeline.

| Day | Date | Focus | Deliverable |
|---|---|---|---|
| 1 | Thu 15 May | Project skeleton + paperwork | dotnet solution running, Postgres docker up, Meta paperwork submitted |
| 2 | Fri 16 May | Domain models + message channel abstraction | Ticket/Resident/Message records, EF migrations, MockChannel + IMessageChannel |
| 3 | Sat 17 May | Agent abstraction base | AgentBase<TIn,TOut>, IAnthropicClient impl, ExecutionContext, retry/escalation logic |
| 4 | Sun 18 May | First concrete agent | ClassifierAgent + prompt v1, manifest.yaml, 5+ unit tests |
| 5 | Mon 19 May | Orchestrator + persistence | TriageOrchestrator (Classifier-only), TicketRepository, end-to-end integration test |
| 6 | Tue 20 May | Real channel + real LLM | TelegramAdapter live, Serilog config, first end-to-end Telegram message → DB ticket |
| 7 | Wed 21 May | Eval suite + clarification | xUnit eval runner, 15+ classifier cases, %80+ baseline, clarification flow |

**Phase 1 DoD (Day 7 akşamı):**
- ✅ Telegram bot çalışıyor (test grubu)
- ✅ Mesaj → DB'de ticket görülebiliyor
- ✅ Classifier eval score ≥%80
- ✅ Tüm decision'lar structured log'da
- ✅ Repo public/private kararı verilmiş
- ✅ Decision journal başlamış

### Phase 2: Real Integration (Days 8-14) — Full Pipeline + WhatsApp

**Hedef:** 3 agent tam pipeline + WhatsApp adapter + babanın binasına deploy hazır.

| Day | Date | Focus | Deliverable |
|---|---|---|---|
| 8 | Thu 22 May | EnricherAgent + vector search | pgvector setup, ONNX embedding, similarity search, EnricherAgent + prompt v1 |
| 9 | Fri 23 May | RouterAgent + emergency fast-path | Rule-based router, emergency keyword detector, LLM fallback |
| 10 | Sat 24 May | WhatsApp adapter | Meta Cloud API webhook, signature verification, template approval submission |
| 11 | Sun 25 May | Dashboard (Razor Pages) | Ticket list, filter by status/priority, agent decision audit view |
| 12 | Mon 26 May | Production hardening | Error handling, idempotency, webhook retry handling, baba brief |
| 13 | Tue 27 May | KVKK + deploy prep | Disclosure mesaj template, Fly.io deploy script, env config, secrets management |
| 14 | Wed 28 May | LIVE DEPLOY 🚀 | Babanın grubuna WhatsApp bot eklendi, disclosure gönderildi, monitoring çalışıyor |

**Phase 2 DoD (Day 14 akşamı):**
- ✅ 3 agent + emergency fast-path canlı
- ✅ WhatsApp Cloud API ile gerçek mesajlar geliyor
- ✅ Babanın binası deployment yapıldı
- ✅ Dashboard'da gerçek ticket'lar görülebiliyor
- ✅ KVKK disclosure done

### Phase 3: Real Usage & Polish (Days 15-21) — Loom Ready

**Hedef:** 5-7 gün gerçek kullanım, edge case yakalama, prompt iterasyonu, Loom çekimi.

| Day | Date | Focus | Deliverable |
|---|---|---|---|
| 15 | Thu 29 May | Day 1 of real usage | Monitor, capture issues, decision journal güncel |
| 16 | Fri 30 May | Edge case mining + prompt v2 | İlk gerçek edge case fix, classifier prompt v2, eval re-run |
| 17 | Sat 31 May | Continued iteration | 2-3 daha edge case, enricher prompt v2 muhtemelen |
| 18 | Sun 1 Jun | LOOM PREP START | Outline 5 maddeyi 10 dakikaya sığdır, demo data hazırla, mikrofon/ortam testi |
| 19 | Mon 2 Jun | Loom prova + polish | İlk full prova (kendine kayıt), zayıf bölümleri tekrar, kod walkthrough netleştir |
| 20 | Tue 3 Jun | Final kayıt + pre-screen | Final Loom kaydı, bir arkadaşa pre-screen, geri bildirim al |
| 21 | Wed 3 Jun (akşam) | GÖNDERİM | Loom + concise email → outreach@example.com |

**Phase 3 DoD (Day 21):**
- ✅ ≥30 gerçek WhatsApp mesajı pipeline'dan geçmiş
- ✅ En az 3 gerçek edge case yakalanmış ve çözülmüş
- ✅ Engineering journal'da Q4 için 5+ "rebuilt differently" notu
- ✅ Loom kayıtlı, 10 dakika altı, ses/görüntü temiz
- ✅ Pre-screen feedback alınmış
- ✅ Email gönderilmiş

---

## 6. Token / Budget Forecast

### 6a. Claude.ai Usage (Bizim Çalışmamız)

**Tahmini günlük etkileşim:**
- Yoğun coding günleri (Day 1-7, 8-14): 15-25 substantive turn/gün
- Real usage / polish günleri (Day 15-21): 5-15 turn/gün
- 21 gün toplam tahmin: ~300-400 turn

**Context bilgisi:** Mevcut konuşmamız (kararlar + planlama) ~80K token civarında. Yeni "build" konuşmaları daha kısa context ile başlayabilir (Projects feature ile baseline azaltılabilir).

**Plan önerisi:**
- **Pro ($20)** → 21 gün için **sıkışıksın**. Pro'da uzun context'te 15-20 mesaj sonra 5-saat beklemen muhtemel. Günde 3-4 productive session = 60-80 mesaj. Yetebilir ama günde 2-3 kez "limit doldu" ile karşılaşma riski yüksek.
- **Max 5x ($100)** → 21 gün için **rahat**. Önerim bu plan, project sonrası downgrade. Net ek maliyet: ~$70.
- **Max 20x ($200)** → Overkill, gerek yok.

**Tactical optimizations (her planda):**
1. **Project feature kullan** — proje context'ini Project knowledge'a koy, her mesajda re-process etmesin.
2. **Yeni konu = yeni konuşma** — agent abstraction sohbeti ile WhatsApp webhook sohbeti ayrı thread'lerde olsun.
3. **Soruları batch'le** — 3 küçük soru = 1 birleşik mesaj.
4. **Kod attach'larken hedefli ol** — tüm projeyi değil, ilgili dosyayı paste'le.

### 6b. Anthropic API Budget ($75 Sınırı — Apartman Triage Sistemi)

**Model dağılımı (planlanan):**
- Classifier (~%70 traffic): Haiku 4.5 — $1/$5 per MTok
- Enricher (~%25 traffic): Haiku 4.5 default, Sonnet escalation (~%30 of enricher = ~%7.5 total): Sonnet 4.6 — $3/$15
- Router (~%5 traffic, LLM fallback only): Haiku 4.5

**21-gün tahmini token kullanımı:**

| Faz | Mesaj/Çağrı | Avg input/output tokens | Tahmini maliyet |
|---|---|---|---|
| Dev iteration (Day 1-14) | ~500 calls (eval + manual test) | 1500 in / 400 out | ~$2-4 |
| Real deployment (Day 14-21) | ~100 mesaj × 3 agent = 300 calls | 2000 in / 500 out | ~$1-2 |
| Eval suite runs (Day 7+) | ~5 full runs × 15 cases × 3 agents | 1500 in / 400 out | ~$1 |
| Edge case debug + retry | Buffer | — | ~$5-10 |
| **Toplam tahmin** | | | **~$10-20** |

**Maliyet kontrol (kuralları)**:
1. Dev sırasında **response caching aktif** (aynı test input → cache hit)
2. Eval suite manuel trigger, her commit'te değil
3. Anthropic Console'da **$30 hard limit** kur (bütçenin %40'ında alarm)
4. Production caching: system prompt'lar cacheable, %90 input savings
5. Batch API (eval suite için): %50 savings

**Sonuç:** $75 bütçe **fazlasıyla yeterli**, muhtemelen $25 altında biteceksin.

### 6c. Diğer Maliyetler

| Kalem | Tahmini Maliyet |
|---|---|
| Fly.io hosting | $0 (free tier yeterli) |
| WhatsApp Cloud API | $0 (ilk 1000 service conversation/ay free) |
| Domain (opsiyonel) | $0-3 |
| Meta Business Verification | $0 |
| **Toplam non-LLM** | **~$0-5** |

---

## 7. Engineering Journal Template

Her gün 15 dakika, bir markdown dosyasına:

```markdown
## Day X — [Date]

### Bugün yapılanlar
- [bullet list]

### Beklemediğim problem / sürpriz
- [eğer varsa]

### Aldığım karar + sebep
- Karar: ...
- Neden: ...
- Alternatif: ...

### Keşke önceden bilseymişim
- [Loom Q4 için altın]

### Yarın
- [next-day intent]
```

**Bu dosya, Loom Q3 ve Q4'ün hammaddesi.** Day 18 prep'te bu journal'ı tarayıp en güçlü 1-2 hikâyeyi seçeceksin.

---

## 8. Loom Final Structure (Day 18 outline)

10 dakika hedefli (≤9:30 final süre):

| Süre | İçerik | the target's Question |
|---|---|---|
| 0:00-0:30 | Intro + project context | — |
| 0:30-2:00 | Problem + motivasyon | Q1 |
| 2:00-4:30 | Architecture + 2-3 key decisions | Q2 |
| 4:30-6:30 | Biggest edge case + solution | Q3 |
| 6:30-7:30 | What I'd rebuild differently | Q4 |
| 7:30-9:00 | Repo structure + separation of concerns + scaling | Q5 |
| 9:00-9:30 | Wrap + invite for deeper dive | — |

---


**Doküman versiyonu:** v2 — 14 May 2026
**Bir sonraki güncelleme:** Day 7 sonu (Phase 1 retrospective)

---

## Pending Updates (Day 7 retro'da formal işlenecek)

Bu bölüm sıcak değişiklikleri yansıtır. Day 7 Phase 1 retrospective sırasında roadmap'in ana gövdesine işlenir.

- **§1 Stratejik Kararlar — Hosting:** "Hosting: Fly.io free tier" → "Hosting: Fly.io free tier (app), Neon Postgres (DB, Frankfurt, pgvector pre-installed)". Karar tarihi: Day 3 (17 May). Gerekçe: journal/day-03.md. Trade-off: KVKK md. 9 yurt dışı veri transferi (Day 9 Security & Compliance review'a parking lot, mitigation: açık rıza Day 13 disclosure).

- **§4 Risk Register — Yeni satır:** "Eski donanım + yeni SDK uyumsuzluğu" (örn. macOS 12 Tier 3, Docker Desktop). Olasılık: Düşük, Etki: Orta. Mitigation: cloud-first fallback strategy (Neon proof of concept).

- **§5 Day-by-Day Roadmap — 1 günlük kayma:** Phase 1 DoD Day 7 → Day 8. Day 4 = Agent abstraction (orijinal Day 3 işi), Day 5 = ClassifierAgent (orijinal Day 4), Day 6 = Orchestrator + Telegram başlangıç, Day 7 = Telegram tamam + Eval suite başlangıç, Day 8 = Phase 1 closing + Phase 2 (Enricher) başlangıç (overlap).
