# Hanwas AI

**An agentic maintenance-request triage system for residential buildings, built on .NET 8 and Claude.**

[![CI](https://github.com/arslan-kursad/apartment-triage/actions/workflows/ci.yml/badge.svg)](https://github.com/arslan-kursad/apartment-triage/actions/workflows/ci.yml)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![C#](https://img.shields.io/badge/C%23-12-239120)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16%20%2B%20pgvector-336791)
![Status](https://img.shields.io/badge/status-production-success)
![License](https://img.shields.io/badge/license-MIT-blue)

> CI runs the full suite except tests requiring a live LLM API key (`Smoke`/`Eval` traits),
> which are documented and run locally — see [`.github/workflows/ci.yml`](.github/workflows/ci.yml).
> One test is skipped on purpose: `ec0020` caught a real similarity-signal bug in the
> embedding pipeline, tracked in [ADR-0015](docs/decisions/ADR-0015-enricher-similarity-threshold-blocked-by-tokenizer.md)
> and [issue #5](https://github.com/arslan-kursad/apartment-triage/issues/5) — not silenced, just not re-blocking every push until that's fixed.

---

## Overview

Building managers drown in unstructured maintenance messages — a water leak, a broken
elevator, and a noise complaint all arrive as free-form text across WhatsApp and Telegram,
in mixed languages, with no structure and no prioritization.

Hanwas AI turns that stream into triaged, actionable tickets. An incoming message is
classified, enriched with context from past tickets, and routed — automatically flagging
emergencies and escalating only ambiguous cases to a stronger model. A manager works from a
real-time dashboard instead of a chat backlog.

The system runs in production for a small real building, processing live resident messages.

Want to try it? Message [@HanwasBot](https://t.me/HanwasBot) on Telegram with a maintenance-style
complaint (Turkish or English) and watch it get triaged.

---

## Key Features

- **Multi-agent pipeline** — a custom orchestrator coordinates three specialized agents; no agent framework.
- **Native multi-channel** — WhatsApp Cloud API and Telegram Bot API, integrated directly.
- **Emergency fast-path** — a two-layer architecture guarantees emergencies are never silently downgraded.
- **Image analysis** — residents can attach a photo; it is sent to the vision model as context.
- **Cost-aware model routing** — Haiku 4.5 by default; only low-confidence cases escalate to Sonnet.
- **Semantic similarity** — local ONNX embeddings + pgvector surface related past tickets.
- **Passwordless auth** — channel-native OTP login (the manager triggers `/login` from the bot), role-ready.
- **Human-in-the-loop replies** — managers can respond to residents from the dashboard.
- **Bilingual** — Turkish/English throughout, with autodetection.

---

## Architecture

Four layers with a strict inward dependency flow (`Web → Application + Infrastructure → Domain`).
Infrastructure implements the interfaces the Application layer declares (Dependency Inversion).

```mermaid
flowchart TB
    subgraph Channels
        WA[WhatsApp Cloud API]
        TG[Telegram Bot API]
    end

    subgraph Web[ASP.NET Core 8]
        WH[Webhook / long-poll]
        JOB[Hangfire job]
        DASH[Razor Pages dashboard]
    end

    subgraph App[Application Layer]
        ORCH[TriageOrchestrator]
        CLS[ClassifierAgent]
        ENR[EnricherAgent]
        RTR[RouterAgent]
    end

    subgraph Infra[Infrastructure]
        ANT[Anthropic API - Haiku / Sonnet]
        ONNX[ONNX embeddings]
        REPO[EF Core repositories]
    end

    DB[(PostgreSQL + pgvector)]

    WA --> WH
    TG --> WH
    WH --> JOB
    JOB --> ORCH
    ORCH --> CLS --> ENR --> RTR
    ENR -.->|similarity| ONNX
    CLS -.->|LLM| ANT
    ENR -.->|LLM| ANT
    RTR -.->|LLM| ANT
    RTR --> REPO --> DB
    DASH --> DB
    DASH -.->|reply| WA
    DASH -.->|reply| TG
```

---

## Agent Pipeline

Each agent has one responsibility and a typed `IAgent<TIn, TOut>` contract. The orchestrator
runs them in sequence, escalating model strength only when the work warrants it.

```mermaid
flowchart LR
    MSG["Incoming message"] --> CLS

    subgraph CLS["ClassifierAgent · Haiku 4.5"]
        C1["Category + severity"]
        C2["Emergency signal"]
        C3["Confidence level"]
    end

    CLS --> ENR

    subgraph ENR["EnricherAgent"]
        E1["ONNX embedding"]
        E2["pgvector similarity"]
        E3["Context from past tickets"]
        E4{"Low confidence?"}
        E4 -->|yes| E5["Escalate to Sonnet 4.6"]
    end

    ENR --> RTR

    subgraph RTR["RouterAgent"]
        R1{"Emergency?"}
        R1 -->|yes| R2["Fast-path route"]
        R1 -->|no| R3["Rule-based route"]
        R3 --> R4["LLM fallback if unresolved"]
    end

    RTR --> TICKET[("Ticket persisted + routed")]
```

---

## Message Flow

```mermaid
sequenceDiagram
    actor Resident
    participant Channel as WhatsApp / Telegram
    participant Web as Webhook / Poll
    participant Job as Hangfire Job
    participant Orch as TriageOrchestrator
    participant Agents as Classifier → Enricher → Router
    participant DB as PostgreSQL
    participant Mgr as Manager (Dashboard)

    Resident->>Channel: Maintenance message (+ optional photo)
    Channel->>Web: Inbound update
    Web->>Job: Enqueue
    Job->>Orch: Process message
    Orch->>Agents: Classify, enrich, route
    Agents->>DB: Persist triaged ticket
    Orch->>Channel: Acknowledge / clarify
    Channel->>Resident: Bot reply
    Mgr->>DB: Review tickets (real-time)
    Mgr->>Channel: Human reply (HITL)
    Channel->>Resident: Manager response
```

---

## Tech Stack

| Area | Choice |
|------|--------|
| Runtime | .NET 8, C# 12 |
| Web | ASP.NET Core 8 Minimal API + Razor Pages, Tailwind CSS |
| AI | Anthropic API (Claude Haiku 4.5 / Sonnet 4.6) via direct `HttpClient` |
| Embeddings | ONNX Runtime, `multilingual-e5-small` (local inference) |
| Data | PostgreSQL 16 + pgvector, EF Core 8, Npgsql |
| Background jobs | Hangfire (PostgreSQL-backed) |
| Channels | WhatsApp Cloud API, Telegram Bot API (both direct) |
| Auth | Cookie authentication, channel-native OTP |
| Logging | Serilog (structured JSON) |
| Testing | xUnit, FluentAssertions, Testcontainers |
| Hosting | Fly.io (Frankfurt), Neon (managed Postgres) |

---

## Data Model

```mermaid
erDiagram
    RESIDENT ||--o{ MESSAGE : sends
    RESIDENT ||--o{ TICKET : owns
    MESSAGE  ||--o| TICKET : source

    RESIDENT {
        string Id
        string DisplayName
        string WhatsAppNumber
        int TelegramId
        string Role
        bool IsActive
    }
    MESSAGE {
        string Id
        string ChannelType
        string RawText
        string ReceivedAt
    }
    TICKET {
        string Id
        string Category
        string Severity
        bool IsEmergency
        string RoutingAction
    }
    OTP_CHALLENGE {
        string Id
        string Identifier
        string CodeHash
        string ExpiresAt
        string ConsumedAt
    }
```

---

## Project Structure

```
src/
  ApartmentTriage.Domain/         Pure entities, enums, value objects
  ApartmentTriage.Application/    Agent abstractions, orchestrator, use cases
  ApartmentTriage.Infrastructure/ EF Core, Anthropic client, channels, ONNX
  ApartmentTriage.Web/            Minimal API + Razor Pages + Hangfire host
tests/
  ApartmentTriage.Tests/          Unit and integration tests
docs/
  decisions/                      Architecture Decision Records
```

The layering is deliberate: the Domain has no dependencies, the Application layer owns the
contracts, and Infrastructure provides the implementations — keeping the LLM, database, and
channel details swappable and the core logic testable in isolation.

---

## Engineering Decisions

Key trade-offs are documented as **14 Architecture Decision Records** in
[`docs/decisions/`](docs/decisions/). A few representative ones:

- [ADR-0001](docs/decisions/ADR-0001-custom-orchestrator-over-semantic-kernel.md) — a ~500-LOC custom orchestrator over a general agent framework, for transparency and control.
- [ADR-0002](docs/decisions/ADR-0002-anthropic-direct-http-over-official-sdk.md) — calling the Anthropic API over direct `HttpClient` instead of an SDK.
- [ADR-0005](docs/decisions/ADR-0005-two-layer-emergency-architecture.md) — a two-layer emergency architecture so a misclassification can never silently drop an emergency.

---

## Getting Started

**Prerequisites:** .NET 8 SDK · PostgreSQL 16 with the `pgvector` extension · the embedding model
(`scripts/download-models.sh`).

```bash
# Configure secrets (user-secrets in dev)
dotnet user-secrets set "ConnectionStrings:Default" "<postgres-connection>"
dotnet user-secrets set "Anthropic:ApiKey"          "<anthropic-key>"
dotnet user-secrets set "TelegramBot:Token"         "<telegram-bot-token>"
dotnet user-secrets set "Embeddings:ModelPath"      "<path-to-onnx-model>"

# Run (migrations apply automatically on startup)
dotnet run --project src/ApartmentTriage.Web
```

```bash
dotnet test    # unit + integration suites
```

---

## Status & Metrics

- **Classification eval** (48 labeled cases): **93.3%** category accuracy, **100%** emergency
  recall, **75%** emergency precision.
- **Production:** live for a small real building, processing real resident messages.
- **Cost:** Neon's Postgres free tier + a single small Fly.io machine (auto-suspended when
  idle); LLM spend is tracked in the dashboard's FinOps view, kept low by the
  Haiku-default / Sonnet-on-escalation routing.

> Metrics are real measurements from the evaluation suite, not projections.
