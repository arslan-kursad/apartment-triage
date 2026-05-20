# Apartman Triage AI — Conversation Organization Chart

**Versiyon:** v2 — 14 May 2026 (Day 0)
**Sonraki güncelleme:** Day 1 akşamı

## Lejant

| Sembol | Durum |
|---|---|
| ✅ | Kurulu ve aktif |
| 🟢 | Şimdi aç — bir sonraki kurulum |
| ⏳ | Planlı — takvimli, daha sonra |
| 🔒 | Koşullu — tetikleyici şart oluşursa |
| ⚫ | Tamamlandı, arşiv |

---

## 1. Management Layer

- [x] **✅ PM — Apartman Triage AI** *(bu chat — Day 0)*

## 2. Meta-Agent Layer

- [x] **✅ Agent — Comm Coach** *(kuruldu: Day 0)*
- [ ] **🟢 Agent — Prompt Engineer** *(Day 1, 15 May)* ← bir sonraki
- [ ] **⏳ Agent — Architect** *(Day 2, 16 May)*
- [x] **✅ Agent — QA Hunter** *(kuruldu: Day 7, 21 May)*
- [x] **✅ Agent — Security & Compliance** *(kuruldu: Day 7, 21 May)*
- [ ] **⏳ Agent — Decision Journalist** *(Day 15, 29 May)*
- [ ] **⏳ Agent — Loom Producer** *(Day 18, 1 Jun)*

## 3. Build Layer

- [ ] **⏳ Build — Agent Abstraction** *(Day 3, 17 May)*
- [ ] **⏳ Build — Classifier Implementation** *(Day 4, 18 May)*
- [ ] **⏳ Build — Orchestrator + Persistence** *(Day 5, 19 May)*
- [ ] **⏳ Build — Telegram Adapter** *(Day 6, 20 May)*
- [ ] **⏳ Build — Eval Suite** *(Day 7, 21 May)*
- [ ] **⏳ Build — Enricher + Vector Search** *(Day 8, 22 May)*
- [ ] **⏳ Build — Router + Emergency** *(Day 9, 23 May)*
- [ ] **⏳ Build — WhatsApp Adapter** *(Day 10, 24 May)*
- [ ] **⏳ Build — Dashboard** *(Day 11, 25 May)*
- [ ] **⏳ Build — Production Hardening** *(Day 12, 26 May)*
- [ ] **⏳ Build — Deploy + KVKK** *(Day 13-14, 27-28 May)*

## 4. Ops Layer

Operasyonel/administrative thread'ler — paperwork, account setup, infrastructure prep. Agent veya kod build değil.

- [x] **✅ Ops — Meta Business Verification** *(kuruldu: Day 0, paperwork Day 1'de aktif başlar)*

> Gelecekte eklenebilir (gerektiğinde): `Ops — Fly.io Setup`, `Ops — Domain Registration`, vs.

## 5. Conditional Layer

- [ ] **🔒 Founder — Strategic**
  - Tetikleyici: PM scope dışı stratejik karar (timeline uzatma/kısaltma, pivot, kariyer planlama, Day 21 sonrası planlama).
- [ ] **🔒 Post-Loom Liaison**
  - Tetikleyici: Day 21 sonrası, the target olumlu yanıt + görüşme koordinasyonu gerekirse.

---

## Güncelleme Kuralları

| Olay | İşlem |
|---|---|
| Conversation açıldı | `[ ]` → `[x]`, sembol `🟢/⏳` → `✅` |
| Görev tamamlandı, arşivlenecek | `✅` → `⚫` |
| Koşullu rol tetiklendi | `🔒` → `🟢` |
| Yeni rol gerekiyor | İlgili layer'a `🔒` ile ekle, tetikleyici yaz |

## Sürüm Geçmişi

- **v1 — 14 May (Day 0):** İlk versiyon, 4 katman.
- **v2 — 14 May (Day 0):** Ops Layer eklendi (Meta Business Verification için). Comm Coach kurulum işaretlendi.
- **v3 — 21 May (Day 7):** QA Hunter + Security & Compliance kuruldu.