# ADR-0006: Agent Abstraction Design

## Status
Accepted

## Date
2026-05-19

## Context
Apartman Triage AI'ın üç LLM agent'ı (ClassifierAgent,
EnricherAgent, RouterAgent) sequential pipeline içinde
çalışıyor. Her agent:
- Anthropic API'ye HTTP çağrısı yapıyor
- Yapısal output (taxonomy-mapped JSON) üretiyor
- Retry ve escalation logic'e tabi
- Bağımsız test edilmeli (mock-friendly)
- Structured log ile her kararı izlenebilir bırakmalı

Bu agent'lar için ortak bir abstraction katmanı gerekti.
Kararlar Day 0'da strateji olarak belirlenmişti (primer §3:
"Custom IAgent<TIn,TOut> orchestrator, ~300 LOC"), Day 5'te
(19 Mayıs 2026) implement edildi.

Tasarım constraint'leri:
- Stack: .NET 8 / C# (kilitli)
- LLM client: HttpClient + System.Text.Json direct (ADR-0002)
- Framework yok: Semantic Kernel, AutoGen, LangGraph reddedildi
  (ADR-0001)
- DI lifecycle: Singleton + stateless (thread-safety şartı)
- Test isolation: IAnthropicClient mocklanabilmeli
- Over-engineering sınırı: ~300 LOC hedefi

## Decision

Beş bileşenden oluşan custom agent abstraction implement edildi:

**1. IAgent<TIn, TOut> (Application layer)**
```csharp
public interface IAgent<TIn, TOut>
    where TIn : class
    where TOut : class
{
    Task<AgentResult<TOut>> ExecuteAsync(
        TIn input,
        AgentContext context,
        CancellationToken cancellationToken = default);
}
```
Generic constraint `where T : class` — record veya class kabul,
struct yasak (serialization belirsizliği). Return type
`AgentResult<TOut>` — exception-based değil (aşağıda gerekçe).

**2. AgentResult<TOut> + AgentError (Application layer)**
Result-based error handling. Triage pipeline'da "düşük
confidence" bir hata değil, bir sonuç — exception bu
nuance'ı bozar. `AgentErrorKind` enum eval suite scoring
ve bütçe alarmı için kullanılabilir.

**3. AgentBase<TIn, TOut> (Application layer)**
Abstract base class. Sorumlulukları:
- Retry loop: sadece transient error'larda (429, 529, 5xx,
  network). Semantic error (parse failure, schema mismatch)
  immediate return — aynı çağrıyı tekrarlamak bütçe yakar,
  sonuç değişmez.
- Exponential backoff with jitter (thundering herd prevention)
- `protected virtual bool ShouldEscalate(AgentError, int)`
  — agent kendi domain knowledge'ına göre escalation sinyali
  verir, orchestrator eylemi alır (agent-side karar,
  orchestrator-side eylem separation)
- Structured log scope: her attempt, her escalation, her
  failure Serilog ile trace edilir

**4. AgentContext (Application layer)**
Cross-agent correlation için 4 field:

| Field | Gerekçe |
|---|---|
| `CorrelationId` | Pipeline-wide trace (= TraceId) |
| `MessageId` | Hangi domain Message işleniyor — her agent'ın TIn farklı, cross-agent traceability için context'te taşınmalı |
| `ResidentId` | Dashboard query + log correlation için; TIn'e bağlı değil |
| `ReceivedAt` | Mesaj alınma zamanı — agent latency ölçümü (received → processed delta) |

**Neden ExecutionContext değil AgentContext:**
`System.Threading.ExecutionContext` ile runtime ambiguity —
compile-time uyarı vermez, runtime'da beklenmez hata üretir.
`AgentContext` domain'e özgün, açıklayıcı.

**Neden Caller/AttemptNumber/PromptVersion context'te yok:**
- `Caller` — agent kendi adını manifest'ten biliyor,
  context'e geçirilmesi redundant
- `AttemptNumber` — AgentBase retry loop'unda local variable;
  class field yapılması Singleton stateless kuralını ihlal eder
- `PromptVersion` — manifest'ten constructor'da okunuyor,
  structured log'a agent initialization'da push ediliyor

Üçü de structured log'da capture ediliyor — context'i şişirmeye
gerek yok.

**5. IAnthropicClient / AnthropicClient (Application interface,
Infrastructure implementation)**
DIP: interface Application'da, impl Infrastructure'da. Prompt
caching `cache_control: {type: ephemeral}` ile system prompt'lara
uygulanıyor (~%90 input token tasarrufu). `IHttpClientFactory`
üzerinden HttpClient — socket exhaustion prevention.

**DI registration split:**
- `AddAgents()` → Application/DependencyInjection.cs
  (IAgent Singleton registration)
- `AddAnthropicClient()` → Infrastructure/Anthropic/
  AnthropicClientExtensions.cs (IAnthropicClient + HttpClient)
- `AddInfrastructure()` composite — her ikisini çağırır

Bu split test isolation için kritik: test'lerde `AddPersistence()`
+ `AddAnthropicClient(mock)` kullanılabilir, tüm infrastructure
ayağa kaldırılmasına gerek yok.

## Consequences

### Positive
- Agent'lar bağımsız unit test edilebilir: `IAnthropicClient`
  mock ile ClassifierAgent test'i infrastructure'a bağlı değil
- Retry/escalation logic merkezi: her agent implement etmek
  zorunda değil, AgentBase'den inherit eder
- Structured log tutarlı: tüm agent'lar aynı field set'i
  loglar, eval suite ve dashboard aynı schema'yı bekleyebilir
- `AgentResult<TOut>` pipeline semantiğini korur: "düşük
  confidence" exception değil, işlenebilir sonuç
- `AddAgents()` split: test'lerde sadece agent layer ayağa
  kalkar, EF Core + Hangfire + WhatsApp gerekmez
- Loom Q2 (architecture decisions) ve Q5 (separation of
  concerns) için savunulabilir, somut tasarım kararları

### Negative / Trade-offs
- ~300 LOC custom orchestration (ADR-0001 ile öngörülmüştü)
  — her yeni agent tipi için AgentBase extend edilmeli
- `AgentResult<TOut>` tüm caller'ların IsSuccess kontrolü
  yapmasını gerektiriyor — exception-based'e göre
  "fail loudly" garantisi daha az (discipline şart)
- `ShouldEscalate` protected virtual: concrete agent override
  etmezse AgentBase default'u devreye girer — default'un ne
  olduğu açık belgelenmeli (şu an `return false`)
- Manifest YAML parsing her agent initialization'ında — lazy
  load yok, uygulama başlangıcında tüm manifest'ler yüklenir
  (3 agent için önemsiz, 20+ agent'ta reconsidered)

### Neutral
- `AgentErrorKind` enum şu an 6 değer — eval suite ve
  routing logic matürleştikçe genişleyebilir (Architect flag)
- Day 5 itibariyle ClassifierAgent için concrete implementation
  yok — abstraction test edildi, concrete Day 6'ya kaldı
- `protected virtual ShouldEscalate` — TestAgent override
  ile test edildi; production ClassifierAgent escalation rules
  Day 7 eval suite'te kalibre edilecek

**Footnote — DI registration pattern (Day 5, Architect kararı):**
Aynı `IAgent<TIn,TOut>` tipinin iki model varyantını (Haiku/Sonnet)
DI'a register etmek için .NET 8 Keyed Services tercih edildi.
Factory pattern (3 agent × 2 model = 6 factory class) over-engineering,
strongly-typed subclass anti-pattern. `AgentKeys` static class
Application layer'da sabit key'leri tutar; `AddKeyedSingleton` +
`[FromKeyedServices]` attribute ile zero extra dependency.
Day 8 Enricher eklendiğinde aynı pattern, `AgentKeys`'e 2 sabit eklenir.

## Alternatives Considered

### Alternative A: Exception-based error handling
Agent'lar `AgentExecutionException` throw eder, caller'lar
try/catch ile yakalar.

Rejected because: Triage pipeline'da "düşük confidence ile
sınıflandırıldı" bir hata değil, routing kararı. Exception
bu semantik farkı kaybettirir. `AgentErrorKind.SchemaInvalid`
ile `AgentErrorKind.Transient` caller'da farklı işlenir —
exception hierarchy ile bu ayrım try/catch bloklarına dağılır,
merkezi bir yerde tutulamaz.

### Alternative B: Semantic Kernel IKernelPlugin
Semantic Kernel'ın plugin abstraction'ı, KernelFunction attribute'ları.

Rejected because: ADR-0001'de reddedildi. Anthropic native
desteği sınırlı, framework öğrenme maliyeti, orchestration
üzerinde kontrol kaybı. Bu ADR ADR-0001'in implementasyon
seviyesindeki devamı.

### Alternative C: MediatR pattern (IRequest/IRequestHandler)
Her agent bir MediatR handler, pipeline behavior'lar retry +
logging için.

Rejected because: MediatR yeni NuGet dependency (Architect flag
şartı, stack lock). Retry ve logging'in generic pipeline
behavior içinde yaşaması test edilebilirliği azaltır —
her behavior ayrı test setup ister. `IAgent<TIn,TOut>` aynı
contract'ı ~30 LOC ile sağlıyor.

### Alternative D: AgentContext'te Caller + AttemptNumber + PromptVersion
Orijinal Architect önerisi: `TraceId, Caller, AttemptNumber,
PromptVersion`.

Superseded by current design because: `Caller` manifest'ten
agent'ın kendi bilgisi, `AttemptNumber` retry loop'ta local
(Singleton stateless korunur), `PromptVersion` manifest'ten
constructor'da okunup log'a push ediliyor. `MessageId` ve
`ResidentId` cross-agent correlation için daha değerli —
her agent'ın TIn tipi farklı, bu bilgileri TIn'de aramak
context-switching maliyeti yaratır. `ReceivedAt` latency
measurement için pipeline'a özgün. 4 field korundu,
içerik refine edildi.

## References
- ADR-0001 (Custom Orchestrator over Semantic Kernel)
- ADR-0002 (Anthropic Direct HTTP over Official SDK)
- apartment_triage_roadmap.md §1 (Stratejik Kararlar —
  custom IAgent<TIn,TOut> orchestrator ~300 LOC)
- claude_project_primer.md §3 (Sorgulanmayacak Kararlar)
- journal/day-05.md (Day 5 implementation notları)
- CLAUDE.md §Stack (kapanmış kararlar)
