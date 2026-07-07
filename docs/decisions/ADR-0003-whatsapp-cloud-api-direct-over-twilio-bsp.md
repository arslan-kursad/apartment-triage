# ADR-0003: WhatsApp Cloud API Direct over Twilio BSP

## Status
Accepted

## Date
2026-05-14

## Context
Apartman Triage AI'ın birincil input kanalı WhatsApp. Türkiye'de
apartman sakinlerinin kullandığı dominant mesajlaşma platformu
WhatsApp olduğu için kanal seçimi Day 0'da kilitlendi.

WhatsApp entegrasyonu için iki temel yol mevcut: Meta'nın
WhatsApp Business Cloud API'sine doğrudan erişim veya Twilio
gibi BSP (Business Solution Provider) üzerinden erişim.

Constraint'ler:

- Deployment: gerçek bir bina (18 daire, 11 sakin) — küçük ölçek
- İlk 1000 service conversation/ay Meta'da ücretsiz
- Twilio BSP maliyeti küçük ölçekte orantısız
- Meta Business Manager verification süreci zaten
  başlatılacak — BSP ek bir abstraction katmanı ekliyor
- Telegram fallback (dev/test) adapter pattern ile hazır
- Loom demo'sunda "Meta ile doğrudan entegrasyon" anlatısı
  BSP'ye göre daha güçlü mühendislik kanıtı

## Decision
Twilio BSP kullanılmadı. Meta WhatsApp Business Cloud API'ye
doğrudan HTTP entegrasyonu implement edildi. IMessageChannel
abstraction'ı adapter pattern ile yazıldı; Telegram fallback
aynı interface üzerinden çalışıyor.

## Consequences

### Positive
- İlk 1000 service conversation/ay ücretsiz — deployment
  maliyeti sıfır
- BSP margin'i yok: Meta fiyatlandırması doğrudan geçerli
- Webhook signature verification, template approval,
  message type handling üzerinde tam kontrol
- IMessageChannel adapter pattern: WhatsApp ↔ Telegram
  geçişi orchestrator'ı etkilemiyor
- Meta Business Manager ilişkisi doğrudan — BSP lock-in yok

### Negative / Trade-offs
- Meta Business Manager verification süreci 7+ gün
  sürebilir (risk register'da: Orta olasılık / Yüksek etki)
- Webhook setup, signature verification, phone number
  management manuel yapılandırıldı — Twilio bu complexity'yi
  soyutluyor
- Meta API breaking change'leri veya policy değişiklikleri
  doğrudan etkiliyor
- Template approval süreci Meta ile doğrudan yürütülüyor
  (BSP desteği yok)

### Neutral
- Telegram fallback risk mitigation olarak hazır; adapter
  pattern bu kararı mümkün kıldı
- Twilio'nun .NET SDK'sı mevcut ama bu proje doğrudan
  HTTP tercih ediyor (ADR-0002 ile tutarlı)

## Alternatives Considered

### Alternative A: Twilio BSP
WhatsApp Business API'ye Twilio üzerinden erişim. .NET SDK,
managed webhook, number provisioning sağlıyor.

Rejected because: Küçük ölçek için maliyet orantısız;
BSP abstraction'ı kontrol kaybı yaratıyor; Twilio SDK
bağımlılığı ADR-0002'nin direct HTTP prensibiyle çelişiyor.

### Alternative B: 360dialog veya benzeri BSP
Avrupa merkezli alternatif BSP'ler.

Rejected because: Twilio ile aynı gerekçe — ek abstraction
katmanı, maliyet, lock-in.

## References
- claude_project_primer.md §3 (Sorgulanmayacak Kararlar)
- apartment_triage_roadmap.md §1 (Input kanalı: WhatsApp
  Business Cloud API)
- apartment_triage_roadmap.md §4 (Risk Register — Meta
  verification 7+ gün)
- ADR-0002 (Anthropic Direct HTTP over Official SDK)
