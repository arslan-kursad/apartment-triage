# ADR-0012 — Dashboard Modernization & HITL Reply

**Status:** Proposed  
**Date:** 2026-05-30  
**Decider:** Architect  
**Context:** Day 17–19, Loom demo hazırlığı

---

## Bağlam

Mevcut Razor Pages UI Bootstrap 5 tabanlı, sınırlı analitik gösterge, tek dil (TR). Demo (Day 21) için:
- Görsel olarak güçlü bir dashboard
- Gerçek veri metrikler (KPI, chart'lar)
- HITL (Human-in-the-Loop) reply arayüzü
- TR/EN dil desteği

Ayrıca FinOps bölümü için Haiku/Sonnet oranı metriği isteniyor; bu mevcut DB schema'sında bulunmuyor — ayrı FLAG olarak işaretlenmiştir (bkz. §Açık Sorunlar).

---

## Karar

### CDN Seçimi

| Kütüphane | CDN | Neden |
|-----------|-----|-------|
| Tailwind CSS | cdn.tailwindcss.com (play CDN) | Bootstrap'tan daha esnek utility-first; sidebar/grid/responsive kolaylaşıyor |
| Flowbite | cdnjs (2.3.0) | Tailwind uyumlu komponent seti; sidebar toggle, modal, badge |
| Chart.js | cdnjs (4.4.1) | Lightweight, Canvas tabanlı, SSR gerektirmiyor |

**Play CDN Not:** Üretim için `tailwindcss` CLI build önerilir. Loom demo kapsamında play CDN yeterli.

### İstanbul Saat Dönüşümü

```csharp
static IstanbulTime()
{
    try { _tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul"); }
    catch { _tz = null; /* container'da tz-data yoksa */ }
}

public static DateTime FromUtc(DateTime utc)
    => _tz is not null
        ? TimeZoneInfo.ConvertTimeFromUtc(utc, _tz)
        : utc.AddHours(3); // fallback: Türkiye UTC+3, DST yok (2016+)
```

- Sunucu tüm tarihler için `IstanbulTime.Format(utc)` kullanır
- Topbar saati client-side JS ile güncellenir (`Europe/Istanbul` timezone, fallback UTC+3)

### Sayfa Yapısı

```
/                  → Genel Bakış (Index.cshtml) — KPI + charts + feed
/tickets           → Ticket Listesi (mevcut + Tailwind port)
/tickets/{id}      → Ticket Detayı (mevcut + Tailwind port)
/inbox             → Mesajlar (3-panel + HITL)
```

### API Endpoint'leri

Tüm istatistik endpoint'leri minimal API olarak `StatsEndpoints.cs`'te tanımlanır ve doğrudan `ApartmentTriageDbContext`'i enjekte eder (aggregate query'ler repository abstraction'ına uymadığı için direkt DbContext tercih edildi; Repository pattern bozulmadı).

```
GET  /api/stats              → genel sayaçlar
GET  /api/stats/categories   → kategori dağılımı
GET  /api/stats/severity     → şiddet dağılımı
GET  /api/stats/routing      → yönlendirme dağılımı
GET  /api/stats/trends       → günlük trend (14 gün)
GET  /api/tickets/recent     → son 10 ticket (kanal + preview)
GET  /api/eval/summary       → eval metrikleri (statik, Prompt v3 sonrası güncellenir)
POST /api/messages/{id}/reply → HITL reply (FAZ 3)
```

### Dil Desteği (TR/EN)

- localStorage tabanlı: `atriage_lang` = `"tr" | "en"`
- Her metin çifti `<span class="lang-tr">` / `<span class="lang-en">` ile render edilir
- JavaScript `applyLang()` ilgili span'ları göster/gizler
- Server-side içerik (Razor) her iki dili de render eder; display JS tarafından yönetilir

### Eval Summary Stratejisi

`/api/eval/summary` statik C# DTO döner. Değerler `Dashboard:Eval` config section'ından okunur. Prompt v3 eval çalıştırıldıktan sonra `appsettings.json` elle güncellenir. Fabricated metric gösterilmez — config değeri yoksa `null` döner ve UI "Eval bekleniyor" gösterir.

### Teslim Fazları

| Faz | Gün | Kapsam |
|-----|-----|--------|
| FAZ 1 | Day 17 | `_Layout` Tailwind, Genel Bakış, stats endpoints |
| FAZ 2 | Day 18 | Tickets Tailwind port + kanal sembolü + İstanbul saat |
| FAZ 3 | Day 18-19 | Inbox 3-panel + HITL reply endpoint |

Deploy: 3 faz tamamlandıktan sonra tek deploy.

---

## Açık Sorunlar

### FLAG: FinOps Haiku/Sonnet Oranı — Schema Migration Gerekiyor

```
FLAG: FinOps dashboard'da gerçek Haiku/Sonnet API çağrı oranı için
      Ticket entity'sinde EscalatedToSonnet (bool) field gerekiyor.
Context: TriageOrchestrator'da `escalated` bool var ama Ticket'a persist
         edilmiyor. Serilog'da "classifier/claude-haiku-4-5" AgentId var
         ama DB sorgusu yapılamıyor.
Proposed direction: Ticket entity'sine EscalatedToSonnet bool + migration.
                    Cannot Decide kapsamında — Architect onayı bekliyor.
Blocking: FinOps Haiku/Sonnet oranı metriği. Diğer tüm dashboard
          özellikleri bu flag'den bağımsız çalışıyor.
```

Migration onaylanana kadar FinOps kartı Haiku/Sonnet oranını "N/A — pending" gösterir.

---

## Reddedilen Alternatifler

- **Bootstrap 5 kalınsın:** Mevcut tema yeterli değil; Loom demo vizüel gereksinimi karşılamıyor.
- **Blazor:** Locked stack ihlali.
- **Server-Sent Events (canlı feed):** Demo scope'u aşıyor; polling + page reload yeterli.
- **Ayrı eval_runs DB tablosu:** Overengineering; statik config daha pragmatik.
