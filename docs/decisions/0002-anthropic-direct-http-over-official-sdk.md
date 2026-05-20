# ADR-0002: Anthropic Direct HTTP over Official SDK

## Status
Accepted

## Date
2026-05-14

## Context
Apartman Triage AI'ın LLM katmanı Anthropic API'yi kullanıyor.
.NET ekosisteminde Anthropic ile entegrasyon için iki temel
seçenek mevcut: resmi Anthropic .NET SDK veya HttpClient +
System.Text.Json ile doğrudan HTTP çağrısı.

Değerlendirme Day 0'da yapıldı. Constraint'ler:

- Stack: .NET 8 / C# (kilitli)
- LLM provider: Anthropic (kilitli)
- Prompt caching, streaming, tool use gibi Anthropic-specific
  feature'ların tam kontrolü gerekiyor
- SDK'nın versiyon döngüsü ve breaking change riski proje
  timeline'ını etkilememeli
- IAnthropicClient abstraction'ı test edilebilir olmalı
  (mock-friendly)
- Loom demo'sunda "kutunun içini biliyorum" anlatısı için
  HTTP katmanının şeffaf olması tercih ediliyor

## Decision
Anthropic resmi .NET SDK kullanılmadı. HttpClient +
System.Text.Json ile doğrudan Anthropic API çağrısı implement
edildi. IAnthropicClient interface'i custom olarak yazıldı.

## Consequences

### Positive
- Prompt caching header'ları, streaming chunk handling,
  tool use payload'ları üzerinde tam kontrol
- SDK versiyon bağımlılığı yok — Anthropic API breaking
  change'i SDK update beklemeden handle edilebilir
- IAnthropicClient mock'lanabilir, test isolation temiz
- HTTP katmanı şeffaf: her request/response Serilog'a
  structured log olarak düşüyor, SDK soyutlaması arkasında
  kaybolmuyor
- .NET HttpClient'ın retry policy'si (Polly veya custom)
  SDK'nın kendi retry mekanizmasıyla çakışmıyor

### Negative / Trade-offs
- Anthropic API response deserialization manuel yazıldı
  (~100 LOC) — SDK bunu ücretsiz sağlıyor
- Yeni Anthropic feature'ları (yeni model parametresi,
  yeni endpoint) SDK update yerine manuel ekleniyor
- Error handling (rate limit, overload, auth) sıfırdan
  yazıldı

### Neutral
- Anthropic .NET SDK community-maintained durumda,
  official support seviyesi belirsiz — bu risk doğrudan
  HTTP'ye geçiş kararını destekliyor ama belirleyici değil
- Python SDK ile feature parity beklentisi .NET'te
  her zaman gecikmeli — doğrudan HTTP bu gecikmeyi ortadan
  kaldırıyor

## Alternatives Considered

### Alternative A: Anthropic resmi .NET SDK
NuGet üzerinden erişilebilir, temel API çağrılarını
soyutluyor.

Rejected because: Prompt caching ve streaming için
SDK'nın yeterli kontrolü sağlayıp sağlamadığı
doğrulanamadı; mock-friendly interface üretmek SDK
mimarisine bağlı kalıyor; SDK versiyon riski proje
timeline'ını etkileyebilir.

### Alternative B: Semantic Kernel üzerinden Anthropic
Semantic Kernel'ın Anthropic connector'ı ile entegrasyon.

Rejected because: ADR-0001'de Semantic Kernel zaten
reddedildi; bu alternatif ADR-0001 kararına bağımlı.

## References
- claude_project_primer.md §3 (Sorgulanmayacak Kararlar)
- apartment_triage_roadmap.md §2 (Final Tech Stack —
  "LLM Client: HttpClient + System.Text.Json (direct, no SDK)")
- ADR-0001 (Custom Orchestrator over Semantic Kernel)
