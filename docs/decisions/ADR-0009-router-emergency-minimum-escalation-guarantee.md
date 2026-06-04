# ADR-0009: RouterAgent Emergency Minimum Escalation Guarantee

## Status
Accepted

## Date
2026-05-22

## Context
RouterAgent Katman 1, IsEmergency=true ve
EmergencyConfidence High|Medium ise
TriggerEmergency döndürüyor. Low confidence
emergency (EmergencyConfidence=Low) ise
Katman 3 (LLM fallback) devreye giriyor.

Problem: Katman 3 sadece AssignTechnician,
EscalateToManager veya Defer döndürebilir.
TriggerEmergency Katman 3'ten çıkamıyor.

QA Hunter Day 8'de ec-0022 case'i yazarken
bu gap'i tespit etti:

  IsEmergency=true + EmergencyConfidence=Low
  → Katman 3 LLM fallback → potansiyel Defer

"biraz duman var" (ec-0007) gibi mesajlar
Low confidence emergency path'ine düşebilir
ve Defer veya AssignTechnician alabilir.
Bu güvenlik-kritik bir false negative riski.

ADR-0005'te "recall öncelikli" prensibi
benimsenmişti: false negative (emergency
kaçırmak) false positive'den (over-escalation)
daha tehlikeli.

## Decision

Katman 2'ye minimum guarantee rule eklendi
(LowConf emergency Katman 3'e ulaşmadan catch):

  IsEmergency=true AND EmergencyConfidence=Low
  → EscalateToManager

Low confidence emergency hiçbir zaman Defer
veya AssignTechnician alamaz.

Katman 2 rule ordering (fix edildi):
  1. IsEmergency + Low confidence → EscalateToManager
  2. NonActionable               → Archive
  3. Severity=Urgent             → EscalateToManager
  4. AmbiguityReasons non-empty  → NotifyResident
  5. SimilarTickets.Any(>0.90)   → AssignTechnician

## Consequences

### Positive
- Low confidence emergency minimum EscalateToManager
  garantisi — production'da hiçbir zaman Defer olmaz
- ADR-0005 "recall öncelikli" ile tutarlı
- Test edilebilir: ec-0022 (QA Hunter) bu
  guarantee'yi doğruluyor
- Loom Q3 anlatısı: "Test yazarken güvenlik-kritik
  gap fark edildi, minimum guarantee eklendi"

### Negative / Trade-offs
- Low confidence emergency EscalateToManager alır —
  bazı false positive'ler manager'ı meşgul edebilir.
  Trade-off bilinçli: over-escalation < under-escalation.

### Neutral
- TriggerEmergency hala sadece Katman 1'den çıkabilir
  (High|Medium confidence). Low confidence için
  TriggerEmergency vermek over-trigger riski taşır —
  Day 16 eval'de kalibre edilebilir.

## Alternatives Considered

### Alternative A: Katman 1'i genişlet
IsEmergency=true AND ANY confidence → TriggerEmergency.

Rejected because: Low confidence = "belki emergency".
%100 recall için precision tamamen feda edilir.
Saatlik false TriggerEmergency production'da
alarm fatigue yaratır.

### Alternative B: Katman 3 prompt'una kural ekle
LLM fallback prompt'una "emergency şüphesi varsa
minimum EscalateToManager" direktifi.

Rejected because: LLM guarantee vermez — prompt
override edilebilir, hallucination riski var.
Deterministik rule > LLM kural.

## References
- ADR-0005 (Two-Layer Emergency Architecture)
- ec-0007 (biraz duman var — motivating case)
- ec-0022 (QA Hunter gap tespiti — Day 8)
- journal/2026-05-22_day08.md
