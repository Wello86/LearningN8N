<!--
Sync Impact Report
==================
Version change: 1.0.0 → 2.0.0

Rationale for MAJOR bump: This amendment redefines the project's core
architectural pattern (explicit ReAct hybrid-routing requirement replaces the
looser "retrieval-grounded" principle), removes "PoC Scope Discipline" (the
project is no longer framed as scope-limited PoC), replaces the LLM/orchestration
stack (Microsoft Foundry → Semantic Kernel/LangChain.NET with Claude Sonnet 5
named explicitly), and adds a binding output-schema contract (`EscalateToHuman:
true`) plus an entirely new Code Quality & Style section (Clean Architecture,
async/await, strong typing). These are backward-incompatible changes to
previously-ratified principles, not clarifications.

Project renamed (informally, in title only): "AI Shopping Assistant Chatbot" →
"AI-Shopping-Assistant".

Modified principles:
- "I. Retrieval-Grounded Answers" → "I. Hybrid RAG Data Routing (ReAct Pattern)"
  (redefined: now mandates an explicit ReAct reasoning/acting loop for routing
  decisions, not just "answers must come from a real source")
- "II. Confident-or-Honest Fallback" → "VII. Absolute Hallucination Guardrail"
  (redefined: now specifies a strict confidence-threshold trigger and a binding
  `EscalateToHuman: true` schema field, not just "say so and offer escalation")
- "III. Customer-Appropriate Communication" → "VI. Customer-Facing Tone"
  (redefined: now explicitly forbids exposing internal database IDs, with an
  explicit exception carved out for Order IDs)
- "IV. Conversation Logging & Traceability" → "III. Conversation Audit Logging"
  (redefined: now mandates specific audit fields — raw prompts, structured
  queries executed, vector similarity scores, final responses — via dedicated
  middleware, not just "log the turn")

Removed principles:
- "V. PoC Scope Discipline" — dropped; the project is no longer treated as a
  scope-capped proof of concept in this revision. No successor principle
  reintroduces a scope cap; if scope discipline is still desired, it should be
  reintroduced explicitly in a future amendment.

Added principles:
- "II. Context Isolation Between Data and Conversation"
- "IV. Layered Clean Architecture"
- "V. Fully Asynchronous I/O"
- "VIII. Strong Typing Before LLM Reasoning" (implicit strong-typing rule
  promoted to its own principle since it gates what may reach the LLM step)

Added sections:
- Code Quality & Style Standards (new top-level section)

Removed sections:
- "PoC Scope Discipline" cross-references in Development & Review Workflow

Technology stack changes (Section: Technology Stack Constraints):
- Orchestration changed from "Microsoft Foundry hosting a Claude model" to
  "Semantic Kernel or LangChain.NET utilizing Claude Sonnet 5" (explicit model
  pin)
- Backend clarified as ".NET 10 Web API" (previously "a .NET 10 application")
- Database clarified as "PostgreSQL with pgvector for hybrid search
  (structured + vector)" — same substance as v1.0.0, wording tightened
- Frontend unchanged: React.js (SPA UI)

Templates requiring updates:
- ✅ .specify/templates/plan-template.md — Constitution Check gate is derived
  dynamically from this file at plan time; no hardcoded principle names to
  update.
- ✅ .specify/templates/spec-template.md — generic, no principle-specific
  references requiring change.
- ✅ .specify/templates/tasks-template.md — generic, no principle-specific
  references requiring change.
- ✅ Repo scan confirms no specs/, plans/, or other markdown outside this file
  reference the superseded v1.0.0 principle names — nothing else to update.

Follow-up TODOs:
- None. Ratification date preserved from v1.0.0; Last Amended set to the date
  of this session.
-->

# AI-Shopping-Assistant Constitution

## Core Principles

### I. Hybrid RAG Data Routing (ReAct Pattern) (NON-NEGOTIABLE)

The system MUST support strict hybrid data routing: for every user query it MUST
dynamically decide whether to query structured SQL data (Orders), unstructured
vector data (Policies/Products), or both, and it MUST make that decision using a
ReAct (Reasoning and Acting) pattern — reasoning about what information is needed
before acting, then acting (querying), then reasoning again over the results
before producing a final answer. Answers MUST NOT be produced from general LLM
knowledge when a routing decision to structured or vector data was available and
skipped.

**Rationale**: A shopping assistant that blends order facts and policy text must
make an explicit, inspectable decision about where an answer's grounding comes
from — an implicit or single-shot retrieval call cannot reliably support
"mixed/reasoning" questions that require combining both sources.

### II. Context Isolation Between Data and Conversation

LLM prompts MUST structurally separate raw data payloads (SQL query results,
vector search chunks) from the ongoing conversation history/context. Retrieved
data MUST be passed to the model in clearly delineated, purpose-labeled sections
distinct from prior user/assistant turns, and MUST NOT be concatenated into free-
form conversation text in a way that lets one customer's retrieved data bleed into
another session's context or be echoed back as if it were conversation history.

**Rationale**: Mixing raw retrieved data with conversational context is a data
leakage risk — it makes it easy for one customer's order/policy data to surface
in the wrong context, and makes prompt injection via retrieved documents harder
to detect and contain.

### III. Conversation Audit Logging (NON-NEGOTIABLE)

An audit logging middleware MUST record, for every conversation turn: the raw
user prompt, the structured queries executed (including parameters), the vector
similarity scores of any retrieved chunks, and the final LLM response returned to
the user.

**Rationale**: These specific fields are the minimum needed to reconstruct why the
assistant said what it said — which data it considered, how confident the
retrieval was, and what it decided to surface — which is required to debug
hallucinations, tune the routing logic, and audit for data leakage.

### IV. Layered Clean Architecture

The .NET solution MUST be organized as WebAPI (transport/controllers), Core
(domain models and orchestration logic, including the ReAct routing and prompt
construction), and Infrastructure (data access and vector access) projects, with
dependencies pointing inward only — Infrastructure and WebAPI MUST depend on
Core; Core MUST NOT depend on Infrastructure or WebAPI.

**Rationale**: Separating orchestration/domain logic from the concrete database
and vector-store implementations keeps the ReAct routing and prompt-construction
logic testable in isolation and swappable (e.g. changing the vector store)
without rewriting business logic.

### V. Fully Asynchronous I/O

All I/O-bound operations — database queries, vector similarity search, and LLM
calls — MUST use `async`/`await` end-to-end, from the WebAPI controller down
through Core and Infrastructure. Blocking calls (`.Result`, `.Wait()`, sync-over-
async wrappers) on I/O-bound work are prohibited.

**Rationale**: LLM and hybrid-search calls are latency-dominated; a single
blocking call anywhere in the chain undermines throughput and risks thread-pool
starvation under load, and is not something to discover after deployment.

### VI. Customer-Facing Tone

Responses MUST be conversational, empathetic, and clear. Responses MUST NOT
contain raw technical jargon (SQL fragments, vector scores, internal error
messages) or expose internal database identifiers to the end user — with the
explicit exception of Order IDs, which customers already know and reference.

**Rationale**: The end user is a shopper, not an engineer; internal identifiers
and technical detail erode trust and readability, while Order IDs are the one
identifier customers themselves use to refer to their own data.

### VII. Absolute Hallucination Guardrail (NON-NEGOTIABLE)

If the routing/reasoning step's confidence score drops below a defined strict
threshold, or a data lookup (structured or vector) returns empty, the assistant
MUST execute the standard safe fallback: politely state that it cannot find the
information, and set an explicit `EscalateToHuman: true` flag in the response
schema payload. The assistant MUST NOT fill the gap with a plausible-sounding
guess.

**Rationale**: A named, binding schema field (`EscalateToHuman`) — rather than
just a polite sentence — makes the fallback machine-actionable, so downstream
systems can reliably route to a human agent instead of relying on parsing free
text for intent.

### VIII. Strong Typing Before LLM Reasoning

All data routing results (structured query results and vector search results)
MUST be mapped to strict, well-defined domain records before being passed into
the LLM reasoning step. Passing loosely-typed dictionaries, raw JSON, or
untyped query results directly into prompt construction is prohibited.

**Rationale**: Typed domain records make it possible to validate what data is
actually available before it reaches the model, catch shape mismatches at compile
time, and keep Context Isolation (Principle II) enforceable — an untyped blob is
much easier to mishandle across that boundary.

## Technology Stack Constraints

The following stack choices are fixed by the approved architecture and MUST NOT
be substituted without amending this constitution first:

- **Backend**: .NET 10 Web API.
- **Database**: PostgreSQL with pgvector, supporting hybrid search across
  structured (SQL) and vector (embedding) data.
- **Orchestration**: Semantic Kernel or LangChain.NET, utilizing Claude Sonnet 5
  as the reasoning/orchestration model.
- **Frontend**: React.js (SPA UI).

## Code Quality & Style Standards (C# / .NET 10)

- Solution structure MUST follow the Layered Clean Architecture split in
  Principle IV (WebAPI / Core / Infrastructure).
- All I/O-bound code MUST be fully asynchronous per Principle V.
- All data crossing the routing → LLM boundary MUST be strongly typed per
  Principle VIII.

## Development & Review Workflow

- Every feature or change MUST be validated against the Core Architectural
  Guardrails before being considered done: correct hybrid routing behavior
  (Principle I), context isolation (Principle II), and audit logging completeness
  (Principle III).
- Code review MUST explicitly verify: (a) no blocking calls were introduced on an
  I/O path (Principle V), (b) no untyped payloads cross into prompt construction
  (Principle VIII), and (c) the hallucination guardrail and `EscalateToHuman`
  flag are exercised by a test for any new query/response path (Principle VII).
- Reviews MUST treat Principles I, III, and VII as release blockers — a
  regression in hybrid routing, audit logging, or the hallucination guardrail
  MUST NOT ship even if the surrounding feature is otherwise complete.

## Governance

This constitution supersedes ad-hoc practice for the AI-Shopping-Assistant
project. All specs, plans, and task lists produced by `/speckit-*` workflows
MUST be checked against these principles — a violation MUST be either resolved
or explicitly justified in the plan's Complexity Tracking section before
implementation proceeds.

Amendments require: (1) a documented rationale for the change, (2) a version
bump following semantic versioning — MAJOR for backward-incompatible principle
removals or redefinitions, MINOR for new principles or materially expanded
guidance, PATCH for wording/clarification fixes — and (3) an updated Sync Impact
Report prepended to this file recording what changed and why.

**Version**: 2.0.0 | **Ratified**: 2026-07-11 | **Last Amended**: 2026-07-12
