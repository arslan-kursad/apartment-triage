# ADR-0010: Ticket Routing Decision Persistence

## Status
Accepted

## Date
2026-05-23

## Context
Dashboard Ticket detail sayfası (/tickets/{id})
agent kararlarının DB'den okunmasını gerektiriyor.

RouterAgent kararı (RoutingAction) ve
ClassifierAgent ambiguity sinyali (AmbiguityReasons)
Ticket entity'de saklanmıyordu.

Alternatifler:

- Log'dan okuma: Serilog structured JSON'dan
  TicketId ile filtreleme. Fragile, query zorlu,
  log retention bağımlı.

- Ayrı RoutingDecision tablosu: Normalization
  doğru ama Day 9 scope'unda overkill.

- Ticket entity'ye kolon ekle: Additive,
  snapshot, dashboard için yeterli.

Ticket entity'ye ekleme tercih edildi.
AmbiguityReasons için ayrı kolon yerine JSON
blob: max 6 enum değeri, ~150 char gerçek max,
normalization maliyeti karşılamaz.

Dashboard implementasyonu sırasında gereklilik
tespit edildi — acute decision (Migration
üretimi olmadan dashboard tamamlanamıyordu).

## Decision
Ticket entity'ye iki nullable kolon eklendi:

  routing_action           text NULL
  ambiguity_reasons_json   varchar(1000) NULL

SetRoutingDecision(RoutingAction, string?)
setter metodu eklendi. TriageOrchestrator
RouterAgent tamamlandıktan sonra çağırır.

Nullable: pre-router ticket'lar etkilenmez.

## Consequences

### Positive
- Dashboard audit view DB'den okur — log'a
  bağımlı değil, retention riski yok
- Additive migration, mevcut veri korunur
- Setter pattern entity'nin diğer metodlarıyla
  (SetContext, SetNotes) tutarlı
- RoutingAction nullable → pipeline'ın hangi
  aşamada kesildiği DB'den izlenebilir

### Negative / Trade-offs
- AmbiguityReasons JSON blob → type-safe query
  yapılamaz (Day 16'da gerekirse typed column'a
  migrate edilebilir)
- routing_action index yok — dashboard filter
  Status/Category/IsEmergency kullanıyor,
  routing_action filtresi şimdilik yok

### Neutral
- Acute decision: Dashboard implementasyonu
  gereklilik ortaya çıkardı. Önceden planlanmadı
  ama migration additive-only, rollback temiz.

## Alternatives Considered

### Alternative A: Log'dan okuma
RouterAgent kararını Serilog structured log'dan
TicketId ile filtrele.

Rejected because: Log retention bağımlı,
production'da cloud sink olmadan kaybolur,
query API fragile.

### Alternative B: Ayrı RoutingDecision tablosu
FK ile Ticket'a bağlı ayrı tablo.

Rejected because: Day 9 scope'unda overkill.
İki field için ek join maliyeti. Day 16'da
veri büyürse reconsidered.

## References
- Dashboard: /tickets/{id} audit view (ec49b29)
- journal/2026-05-23_day09.md
- ADR-0006 (Agent Abstraction — orchestrator pattern)
