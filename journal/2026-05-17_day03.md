## Day 3 — 17 May 2026 (Pazar)

### Bugün yapılanlar
- Docker bloker akut çözüldü (Day 2'den carry): Neon cloud Postgres'e geçiş (free tier, Frankfurt region, pgvector pre-installed).
- GitHub repo kuruldu: arslan-kursad/apartment-triage (private, KVKK uyumlu).
- gh CLI auth + PR-based workflow aktif. PR #1 (Day 2) + PR #2 (Day 3 ready).
- InitialSchema + AddDomainEntities migration Neon'a uygulandı.
- 6 enum: TicketCategory (taxonomy v3 ile 1:1, 14 değer), TicketSeverity, TicketStatus, ConfidenceLevel, CausalRelation, ChannelType.
- 3 entity: Resident (E.164 + Telegram nullable, partial unique index), Message (kanal-agnostik + MatchedPhrases text[] for Layer 1 audit), Ticket (dual confidence, location_hint, secondary_issues_json, emergency 2-layer flags).
- IMessageChannel abstraction + IncomingMessage record + MockChannel (System.Threading.Channels backed, test fixture API).
- EF Configurations: idempotency unique index (channel_type + external_message_id), dashboard composite index (is_emergency + status).
- dotnet build → 0 Warning, 0 Error.

### Beklemediğim problem / sürpriz
- Docker Desktop macOS 12 Tier 3 uyumsuzluğu (Day 2'de keşfedildi, Day 3'te çözüldü). qemu source build (6-10h) ve eski Docker .dmg (riskli) reddedildi, Neon seçildi.
- Build agent mimari karar (Neon → cloud DB) PM atlayarak aldı. Akut bloker sebebiyle haklı ama foundry yetki sınırı pattern'i tekrar: PE 2x + Build 1x. Hepsinde "karar doğru, prosedür eksik" pattern'i.

### Aldığım karar + sebep
- **Karar:** Neon cloud Postgres (retroactive PM onay).
  - **Neden:** Docker macOS 12 desteğini bıraktı, qemu 6-10 saatlik vakit kaybı, eski .dmg güvenlik riski. Neon 2 dakikada hazır + production-like environment bonus.
  - **Yan kazanç:** Fly.io deploy aşamasında DB için ekstra kurulum yok. docker-compose.yml repo'da yedek olarak duruyor.
  - **Trade-off:** Frankfurt region = yurt dışı veri transferi → KVKK md. 9 kapsamı. Mitigation: açık rıza disclosure'a entegre (Day 13). Day 9 Security & Compliance agent'a parking lot.

- **Karar:** 5 production-mindset karar (Build rapor 5a-e):
  - text[] (PostgreSQL native array) MatchedPhrases için → pgvector operatörleri Day 14+ false_phrase_alarm analizinde
  - SecondaryIssuesJson string blob → premature schema avoidance, Day 8 Enricher'da schema kesinleşecek
  - Restrict cascade → audit trail koruması, KVKK manuel anonymization akışı Day 13'te
  - String enum (HasConversion<string>()) → silent data corruption avoidance + query readability
  - Idempotency erken (unique index, channel_type + external_message_id) → WhatsApp webhook retry önceden bilinen risk

### Keşke önceden bilseymişim
- macOS 12 Tier 3 uyumsuzluğu Day 0 risk register'da yoktu. Genel risk: "eski donanım + yeni SDK = sürpriz uyumsuzluk".
- Build agent sistem prompt'unda "acute decision under pressure" protokolü yoktu — Build doğru kararı verdi ama PM zincirinde kayıt eksik. Day 5'te Architect kurulduğunda foundry sistem prompt template'ine bu bölüm eklenecek.
- Foundation bloğu (Day 2-3) yarım gün gecikme yaptı, ama bu sıkıştırma olsaydı kalite düşerdi. "Yumuşatma kararı doğruydu" pattern.

### Pro Usage Note
- Day 2 + Day 3 birleşik usage: [Code session sonu /status output buraya]
- Day 4 akşamı resmi Pro retro (Day 7 yerine erken).

### Yarın (Day 4, 18 May Pazartesi)
- 08:00 — Meta BM submit (hat açılıyor, verification timer ne kadar erken o kadar iyi)
- 09:00 — PM thread'e gel: Context field rapor + Meta BM durumu + Pro headroom check
- 09:30 — Code session: "Day 4 — Agent Abstraction & Anthropic Client"
- Gün boyu — IAgent<TIn,TOut> + AgentBase + IAnthropicClient (HttpClient + System.Text.Json, prompt caching destekli) + ExecutionContext + retry/escalation
- Risk: Anthropic prompt caching API (cache_control headers) — önce curl test, sonra C# wrap
- Akşam — Pro retro + Day 4 closing

### Parking Lot
- KVKK yurt dışı veri transferi: Neon Frankfurt → AB içi → Türk KVKK md. 9. Day 9 Security & Compliance agent için detay analiz. Çözüm adayı: açık rıza Day 13 disclosure mesajına entegre.
- Roadmap §1 stack güncellemesi: "Hosting: Fly.io" → "Hosting: Fly.io (app), Neon Postgres (DB, Frankfurt, pgvector)". Day 7 retro'da formal update.
- Loom Q4/Q5 hammaddesi (6 hikâye adayı): Docker bloker→Neon abstraction lesson, macOS 12 Tier 3 dev environment risk, SecondaryIssuesJson schema-later pragmatism, Restrict cascade audit-vs-deletion-right, idempotency erken karar, Build foundry yetki kalibrasyonu. Decision Journalist (Day 15) için işlenecek.

### Takvim Durumu
- Phase 1 1 gün kaydı: Day 7 → Day 8 (Phase 2 Enricher ile overlap).
- Phase 2 (7 gün budget) 1 gün sıkıştırılacak.
- Phase 3 sınırı (Day 21 gönderim) etkilenmiyor.
