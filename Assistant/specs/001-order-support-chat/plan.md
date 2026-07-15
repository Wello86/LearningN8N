# Implementation Plan: Order Status & Policy Support Chat

**Branch**: `001-order-support-chat` | **Date**: 2026-07-12 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-order-support-chat/spec.md`

## Summary

Build a customer-facing chat assistant that answers three question types — order
status (structured), policy/product facts (unstructured), and combined
order+policy questions — by having a hand-written ReAct loop in C# decide per
message whether to query Postgres (Orders, via EF Core/Npgsql), query pgvector
(policy/product embeddings, via Pgvector.EntityFrameworkCore), or both, then
synthesize the answer with Claude Sonnet 5 reached through EPAM's CodeMie
gateway. The loop is intentionally hand-rolled rather than framework-driven so
every reasoning/acting step stays single-step debuggable for a POC. A React SPA
provides the chat widget; Postgres+pgvector run locally via Docker Compose.

## Technical Context

**Language/Version**: C# / .NET 10 (backend); TypeScript/JavaScript + React (frontend)

**Primary Dependencies**: ASP.NET Core Web API (.NET 10); EF Core + Npgsql
(Orders data access); Pgvector.EntityFrameworkCore (policy/product vector
store); EPAM CodeMie gateway client (Claude Sonnet 5 access, tool-calling);
React (SPA chat widget)

**Storage**: PostgreSQL — structured `Orders` table via EF Core/Npgsql, plus a
`pgvector`-backed embeddings table for policy/product document chunks, both in
the same database, provisioned locally via Docker Compose

**Testing**: xUnit + FluentAssertions for Core/Infrastructure unit tests;
WebApplicationFactory-based integration tests against a real Postgres+pgvector
instance (Docker) for the WebAPI layer; Vitest + React Testing Library for the
chat widget

**Target Platform**: Linux containers (Docker Compose for local dev; backend is
container-portable), browser (React SPA)

**Project Type**: Web application (React frontend + .NET Web API backend)

**Performance Goals**: No hard SLA specified by the feature spec (POC scope);
target interactive chat latency — a single exchange (including the ReAct
loop's data lookups and the Claude call) should complete in a few seconds,
consistent with SC-001–SC-003's "single chat exchange" expectation

**Constraints**: Must satisfy constitution Principles I–VIII (hybrid ReAct
routing, context isolation, audit logging, layered architecture, async I/O,
customer tone, hallucination guardrail with `EscalateToHuman`, strong typing
into the LLM step); orchestration is a hand-written C# loop rather than
Semantic Kernel/LangChain.NET (see Complexity Tracking — this deviates from
the constitution's Technology Stack Constraints and is flagged for the user)

**Scale/Scope**: POC scale — a small sample Orders dataset (~5–10 orders) and a
handful of policy/product documents (per the feature spec's Assumptions);
single-customer-session concurrency, not designed for production load

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | Hybrid RAG Data Routing (ReAct Pattern) — NON-NEGOTIABLE | PASS | `research.md` §1 and `contracts/react-tooling.md` define the Reason→Act→Observe loop and its two tools; the routing decision is explicit and inspectable at every turn. |
| II | Context Isolation Between Data and Conversation | PASS | `contracts/react-tooling.md` "Message structure" section defines the conversation-history block and the retrieved-data block as structurally distinct message entries, never concatenated. |
| III | Conversation Audit Logging — NON-NEGOTIABLE | PASS | `data-model.md`'s `ConversationTurn` entity carries `UserMessage`, `ToolCallsExecuted`, `RetrievedSimilarityScores`, and `AssistantMessage` — the exact fields Principle III requires — validated by the quickstart's "Audit logging check". |
| IV | Layered Clean Architecture | PASS | Project Structure below splits WebApi / Core / Infrastructure with inward-only dependencies. |
| V | Fully Asynchronous I/O | PASS (to verify at review time) | All contracts (`chat-api.md`, `react-tooling.md`) and data access (EF Core/Npgsql, pgvector) are I/O calls the loop awaits; no design artifact introduces a blocking call. Actual code must still be checked at review per the constitution's Development & Review Workflow. |
| VI | Customer-Facing Tone | PASS | `contracts/chat-api.md` "Response field notes" and `react-tooling.md`'s system-prompt contract forbid internal ids/jargon in `message`, with the Order ID exception. |
| VII | Absolute Hallucination Guardrail — NON-NEGOTIABLE | PASS | `contracts/chat-api.md` defines the guardrail response shape (`escalateToHuman: true` + apologetic message); `react-tooling.md`'s "Loop termination contract" defines the exact triggers (sentinel token, empty lookup, iteration cap), matching `research.md` §4. |
| VIII | Strong Typing Before LLM Reasoning | PASS | `data-model.md` defines typed entities (`Order`, `KnowledgeDocument`) that Infrastructure maps results into before they reach the retrieved-data block described in `react-tooling.md`. |
| — | Technology Stack Constraints (Orchestration: "Semantic Kernel or LangChain.NET") | **VIOLATION — justified** | User's technical direction explicitly hand-rolls the ReAct/tool-calling loop in plain C# instead of Semantic Kernel/LangChain.NET. See Complexity Tracking. |

**Gate result**: 8/8 principles pass after Phase 1 design. One Technology Stack Constraint deviation remains, justified in Complexity Tracking below; it does not block proceeding to `/speckit-tasks`, but the constitution should be amended (`/speckit-constitution`) if this orchestration direction is final, since the current text says orchestration "MUST NOT be substituted without amending this constitution first."

## Project Structure

### Documentation (this feature)

```text
specs/001-order-support-chat/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
backend/
├── src/
│   ├── AiShoppingAssistant.WebApi/           # ASP.NET Core Web API: controllers, DI wiring,
│   │                                         # audit-logging middleware, dev auth header middleware
│   ├── AiShoppingAssistant.Core/             # Domain records, ReAct loop (Reason/Act/Observe),
│   │                                         # prompt construction (context isolation), tool
│   │                                         # definitions, port interfaces (IOrderLookup,
│   │                                         # IPolicySearch, IChatModel), hallucination-guardrail
│   │                                         # + EscalateToHuman logic
│   └── AiShoppingAssistant.Infrastructure/   # EF Core DbContext + Npgsql (Orders, ConversationTurn),
│                                              # Pgvector.EntityFrameworkCore repository (policy/product
│                                              # chunks + embeddings), CodeMie gateway HTTP client
│                                              # (Claude Sonnet 5, tool-calling)
└── tests/
    ├── AiShoppingAssistant.Core.Tests/           # ReAct loop, guardrail, prompt-isolation unit tests
    ├── AiShoppingAssistant.Infrastructure.Tests/ # EF Core/pgvector repository tests (against Docker Postgres)
    └── AiShoppingAssistant.WebApi.IntegrationTests/ # End-to-end chat endpoint tests (US1/US2/US3 + fallback)

frontend/
├── src/
│   ├── components/       # ChatWidget, MessageList, MessageBubble, EscalationBanner
│   ├── pages/             # ChatPage
│   └── services/          # chatApiClient (calls backend chat endpoints)
└── tests/                 # Vitest + React Testing Library component tests

docker/
├── docker-compose.yml     # Postgres (pgvector image) for local dev/integration tests
└── init/
    ├── 01-enable-pgvector.sql
    └── 02-seed-sample-data.sql   # sample Orders + policy/product documents from spec Assumptions
```

**Structure Decision**: Web application split into `backend/` (.NET 10 Web API,
Clean Architecture per constitution Principle IV: WebApi → Core ← Infrastructure)
and `frontend/` (React SPA), with a top-level `docker/` folder for the local
Postgres+pgvector environment. This is Option 2 (frontend + backend) from the
template, with project names and folders made concrete for this feature.

## Complexity Tracking

> Filled because the Constitution Check above surfaced one Technology Stack
> Constraint deviation that must be justified.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|---------------------------------------|
| Orchestration hand-written in plain C# instead of Semantic Kernel or LangChain.NET (constitution's Technology Stack Constraints) | The user's technical direction for this feature explicitly calls for a hand-written tool-calling/ReAct loop "kept simple/debuggable for a POC" — every reason/act/observe step needs to be single-step-through debuggable while the hybrid-routing behavior (constitution Principle I) is being proven out. | Semantic Kernel/LangChain.NET wrap tool-calling and planning in framework-managed internals that are harder to single-step and inspect for a 3-question-type POC of this size; the abstraction's value (multi-tool orchestration at scale, plugin ecosystems) isn't needed yet, and Principle I's own rationale ("an explicit, inspectable decision") is arguably better served by a loop the team wrote and can read line-by-line. |
