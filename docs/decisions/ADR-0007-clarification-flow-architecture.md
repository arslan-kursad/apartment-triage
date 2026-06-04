# ADR-0007: Clarification Flow Architecture — Option C

## Status
Accepted

## Date
2026-05-21

## Context
ClassifierAgent AmbiguityReasons döndürdüğünde
(missing_location, category_ambiguous vs.)
sakin'e açıklama sorusu gönderilmesi gerekiyor.
Bu "clarification reply" için mimari konum kararı
alınması gerekti.

Üç seçenek değerlendirildi:

A1: Orchestrator IResidentRepository +
    IMessageChannel inject eder, Resident DB'den
    yüklenir, TelegramId/WhatsAppNumber alınır.

A2: ITriageOrchestrator.ProcessAsync signature'ı
    genişler (senderExternalId parametresi) —
    interface değişikliği, Architect onayı zorunlu.

C:  TriageResult.AmbiguityReasons expose edilir,
    caller (background consumer job) clarification
    gönderir. Orchestrator değişmez.

Constraint'ler:
- Message.ExternalMessageId = channel message ID,
  gönderici ID değil.
- IncomingMessage.SenderId = gönderici ID,
  caller'da zaten mevcut.
- ITriageOrchestrator interface stabilitesi
  korunmalı.
- Day 10 WhatsApp adapter geleceği için
  channel-agnostic tasarım tercih edilmeli.

## Decision
Option C implement edildi. TriageResult'e
AmbiguityReasons alanı eklendi (additive,
non-breaking). ClarificationTemplates static
class Application/Orchestration/ altında.
ChannelConsumerJob (Web/Jobs/) caller pattern'ını
gösteriyor — Hangfire entegrasyonu Day 9.

## Consequences

### Positive
- Orchestrator tek sorumluluk: classify + route.
  Channel concern'ü caller'a ait.
- ITriageOrchestrator interface değişmedi —
  mevcut caller'lar güncellenmedi.
- Ekstra DB round-trip yok (A1'de gerekirdi).
- Day 10 WhatsApp adapter için channel-agnostic:
  caller hangi channel kullandığını biliyor,
  orchestrator bilmek zorunda değil.
- Message entity'ye SenderExternalId migration
  gerekmedi (A1'de gerekiyordu).

### Negative / Trade-offs
- Clarification gönderme sorumluluğu orchestrator'da
  değil — farklı bir katmanda izlenmeli.
- ChannelConsumerJob Hangfire bağlantısı Day 9'a
  ertelendi: şimdilik skeleton.

### Neutral
- ClarificationTemplates sabit Türkçe metinler.
  Day 16 prompt v2 iteration'ında kalibre edilebilir.
- Priority ordering: MissingLocation >
  CategoryAmbiguous > LanguageUnclear >
  MissingSeverity > NeedsVisual.
  NonActionable → null (mesaj gönderilmez).

## Alternatives Considered

### Alternative A1
Orchestrator'a IResidentRepository + IMessageChannel
inject edilir. Resident DB'den yüklenir, TelegramId
alınır, reply gönderilir.

Rejected because: Orchestrator'a iki yeni dependency,
ekstra DB round-trip, Message entity'ye SenderExternalId
migration gereksinimi. Orchestrator domain concern'ü
dışına çıkıyor.

### Alternative A2
ITriageOrchestrator.ProcessAsync(Message, IMessageChannel,
string senderExternalId, ct) signature genişler.

Rejected because: ITriageOrchestrator public interface
değişikliği — Cannot Decide kapsamı, implement öncesi
Architect onayı zorunluydu. Ayrıca orchestrator
channel-specific bilgi taşımaya başlar.

## References
- ADR-0006 (Agent Abstraction — orchestrator pattern)
- journal/day-08.md
- src/ApartmentTriage.Application/Orchestration/ClarificationTemplates.cs
- src/ApartmentTriage.Web/Jobs/ChannelConsumerJob.cs
