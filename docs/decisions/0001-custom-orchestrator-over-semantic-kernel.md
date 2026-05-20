# ADR-0001: Custom Orchestrator over Semantic Kernel

## Status
Accepted

## Date
2026-05-14

## Context
Apartman Triage AI, birden fazla LLM agent'ının sıralı ve koşullu 
olarak çalıştırıldığı bir triage pipeline'ı gerektiriyor: 
ClassifierAgent → EnricherAgent → RouterAgent, aralarında 
escalation path'leri ve emergency fast-path mantığı mevcut.

Bu pipeline'ı orkestre etmek için bir framework seçimi gerekti. 
Değerlendirme Day 0'da (14 Mayıs 2026) yapıldı. Constraint'ler:

- Stack: .NET 8 / C# (kilitli)
- Agent sayısı: 3 (Classifier, Enricher, Router) — küçük, sabit
- Orchestration karmaşıklığı: lineer pipeline + emergency bypass,
  dynamic agent discovery veya plugin registry gerekmiyor
- LLM provider: Anthropic (Semantic Kernel'ın native provider 
  desteği dışında)
- Timeline: 21 gün, framework öğrenme eğrisi kabul edilemez
- Loom demo hedefi: mimari kararların savunulabilir ve 
  engineering maturity gösteren olması gerekiyor

## Decision
Semantic Kernel (ve diğer agent framework'leri) reddedildi.
~300 LOC custom IAgent<TIn, TOut> orchestrator implement edildi.

## Consequences

### Positive
- Tam kontrol: orchestration davranışı, retry logic, escalation 
  path'leri framework convention'larına bağlı değil
- Anthropic API ile doğrudan entegrasyon — Semantic Kernel'ın 
  Anthropic desteği sınırlı ve community-maintained
- Sıfır framework overhead: pipeline'a girmeyen feature'lar 
  (plugin registry, memory store abstraction, planner) için 
  dependency taşınmıyor
- Loom Q2 (architecture decisions) ve Q5 (separation of concerns) 
  için savunulabilir, özgün karar — "framework kullandım" değil, 
  "şunu neden yazdım" anlatısı daha güçlü
- Test edilebilirlik: IAgent<TIn, TOut> mock'lanabilir, 
  framework'e bağlı test setup yok

### Negative / Trade-offs
- ~300 LOC yazılması gerekiyor (Day 3-5 sprint'ini etkiliyor)
- Retry logic, timeout, circuit breaker sıfırdan yazılıyor — 
  Semantic Kernel bunları sağlıyor
- Gelecekte agent sayısı dramatik artarsa (10+) custom 
  orchestrator yetersiz kalabilir — bu proje için kapsam dışı

### Neutral
- Semantic Kernel bilgisi transferable değil bu projeden — 
  Kürşad'ın .NET + Anthropic kesişim value proposition'ı 
  zaten custom layer'da
- Microsoft.SemanticKernel, AutoGen, Microsoft.Extensions.AI 
  hepsi aynı gerekçeyle reddedildi (framework lock-in, 
  öğrenme maliyeti, Anthropic uyum sorunu)

## Alternatives Considered

### Alternative A: Semantic Kernel
Microsoft'un resmi LLM orchestration framework'ü. Plugin 
sistemi, memory abstraction, planner özellikleri mevcut.

Rejected because: Anthropic provider'ı community-maintained ve 
sınırlı; framework'ün sunduğu feature'ların büyük çoğunluğu bu 
proje scope'unda kullanılmayacak; öğrenme eğrisi 21 günlük 
timeline ile uyumsuz; agent davranışı üzerindeki kontrolü 
azaltıyor.

### Alternative B: LangGraph (.NET port veya Python)
Graph-based stateful agent orchestration. Döngüsel workflow'lar 
için güçlü.

Rejected because: Python değil .NET kilitli; resmi .NET port'u 
production-ready değil; pipeline lineer, graph semantiği 
gereksiz karmaşıklık katıyor.

### Alternative C: AutoGen / Microsoft.Extensions.AI
Multi-agent conversation framework'leri.

Rejected because: Conversation-centric tasarımları bu 
pipeline'ın request-response modeline uymuyor; Anthropic 
entegrasyonu yine sınırlı; aynı öğrenme maliyeti sorunu.

## References
- claude_project_primer.md §3 (Sorgulanmayacak Kararlar)
- apartment_triage_roadmap.md §1 (Stratejik Kararlar tablosu)
- apartment_triage_roadmap.md §2 (Final Tech Stack)
