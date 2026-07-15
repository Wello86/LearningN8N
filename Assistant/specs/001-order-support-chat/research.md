# Phase 0 Research: Order Status & Policy Support Chat

## 1. ReAct loop implementation approach

**Decision**: Hand-write a small, explicit Reason → Act → Observe loop in
`AiShoppingAssistant.Core`, driven by Claude's native tool-use (function-calling)
format, reached through the CodeMie gateway:

1. **Reason**: Send the customer message + recent conversation turns + a fixed
   system prompt + two tool definitions (`get_order_status`, `search_policy_and_product_docs`)
   to Claude Sonnet 5.
2. **Act**: If Claude's response is a tool-use request, the loop executes the
   corresponding port (`IOrderLookup` or `IPolicySearch`) — never both
   speculatively; only the tool(s) Claude actually asked for.
3. **Observe**: Tool results are mapped to typed records (Principle VIII) and
   fed back to Claude in a distinct "Retrieved Data" message block, kept
   separate from the running conversation history (Principle II).
4. Repeat Reason→Act→Observe until Claude returns a final text answer (no more
   tool calls) or the loop's own guardrail short-circuits it (see §4).
5. A hard cap (3 iterations) prevents runaway tool-calling loops from a
   misbehaving model response.

**Rationale**: This directly implements constitution Principle I's routing
requirement without hiding the decision inside a framework's planner, matching
the user's explicit direction to keep the loop hand-written and debuggable.

**Alternatives considered**:
- *Semantic Kernel / LangChain.NET planners* — rejected per user direction and
  Complexity Tracking in plan.md; framework-managed tool orchestration is
  harder to single-step for a POC this size.
- *Single-shot retrieval (always fetch both order + policy data before asking
  the model)* — rejected: wastes a DB/vector round-trip on every fact-based or
  data-based question, and doesn't satisfy Principle I's "dynamically decide"
  requirement.

## 2. Accessing Claude Sonnet 5 through EPAM's CodeMie gateway

**Decision**: Implement `ICodeMieChatClient` in `Infrastructure`, wrapping an
`HttpClient` configured against the CodeMie gateway endpoint. It exposes one
async method (`SendAsync(ChatRequest) -> ChatCompletionResult`) that forwards
Claude Sonnet 5's tool-use request/response shape (system prompt, message
history, tool schemas, tool results) largely as-is, so the Reason/Act/Observe
loop in Core can stay provider-agnostic behind the `IChatModel` port.

**Rationale**: Keeps the CodeMie-specific HTTP/auth details in Infrastructure
(Principle IV), and keeps the tool-use contract close to Claude's native format
so the hand-written loop doesn't need to reimplement translation logic.

**Alternatives considered**:
- *Direct Anthropic SDK, bypassing CodeMie* — rejected: the user's direction
  requires routing through EPAM's CodeMie gateway (org-mandated access path).
- *Generic chat-completion abstraction library* — rejected: adds a dependency
  for a single provider call the POC doesn't need to swap out.

## 3. Embedding model for policy/product documents

**Decision**: Generate embeddings for policy/product document chunks via the
same CodeMie gateway's embedding endpoint (assumed available alongside its
chat models), using a standard embedding model in the 1536-dimension class
(e.g., an `text-embedding-3-small`-equivalent). The `pgvector` column is sized
to that dimension. Embedding generation happens once at data-seed time for the
POC's small, fixed document set (re-embedding on every request is unnecessary
at this scale).

**Rationale**: Reuses the same gateway/credentials already needed for chat,
avoiding a second external provider integration for a POC-scale document set.

**Alternatives considered**:
- *A dedicated local embedding model* — rejected: adds an extra runtime
  dependency (model weights, inference runtime) not justified for ~5 short
  documents.
- *Re-embedding documents on every query instead of at seed time* — rejected:
  wasteful and slower for a static POC document set; revisit if documents
  become dynamic/user-editable later.

## 4. Hallucination guardrail trigger

**Decision**: The loop treats these as guardrail triggers, per constitution
Principle VII and spec FR-007/FR-011: (a) `get_order_status` returns "not
found" for a referenced order, (b) `search_policy_and_product_docs` returns no
chunk above a fixed cosine-similarity threshold, or (c) Claude's own final
answer includes its self-reported low-confidence marker (the system prompt
instructs Claude to emit a specific sentinel token when uncertain instead of
guessing). Any trigger short-circuits the loop into the fallback response:
apologetic "can't confidently answer" message + `EscalateToHuman: true`.

**Rationale**: Combines two objective signals (empty lookups) with one
model-reported signal (explicit uncertainty), rather than trying to derive
"confidence" purely from vector similarity scores, which are useful for policy
search but meaningless for the order-lookup path.

**Alternatives considered**:
- *Similarity-score threshold only* — rejected: doesn't cover order-not-found
  or "policy exists but doesn't actually answer this question" cases.
- *Always ask a second "are you sure?" LLM call* — rejected: doubles latency
  and cost for every turn; the sentinel-token approach gets the same signal in
  the same call.

## 5. Conversation session & memory storage

**Decision**: Persist conversation turns in Postgres (`ConversationTurn` table,
keyed by a `SessionId`) rather than in-memory, since the Web API is otherwise
stateless. Each turn stores the role, message text, referenced order/document
ids, similarity scores, and the `EscalateToHuman` flag — doubling as the audit
log required by Principle III. The Reason step loads the last N turns (a small
fixed window, e.g. 10) for conversation context per FR-012.

**Rationale**: One table serves both "conversation memory for follow-ups" (spec
FR-012) and "audit logging" (constitution Principle III) — no need for a
separate audit store.

**Alternatives considered**:
- *In-memory session state* — rejected: doesn't survive a process restart and
  doesn't satisfy Principle III's durable audit requirement.
- *Separate audit log table + separate conversation-memory table* — rejected as
  unnecessary duplication for a POC; can be split later if audit retention
  policy diverges from conversation-memory retention policy.

## 6. Order ownership verification (FR-010)

**Decision**: For this POC, assume an upstream authentication mechanism
(per spec Assumptions) supplies the requesting customer's id. Since no real
auth system exists yet, stand it in with a minimal dev-only header
(`X-Customer-Id`) validated by WebAPI middleware, with a clear code comment
marking it as a placeholder for the platform's real session/auth integration.
`get_order_status` MUST filter by both the order identifier and this customer
id, returning "not found" (triggering the guardrail) rather than an
authorization-denied message, so as not to reveal that an order exists for a
different customer.

**Rationale**: Satisfies FR-010 today without building a full auth system,
while keeping the seam narrow (one middleware + one repository filter) so it's
a small, obvious swap when real authentication is integrated.

**Alternatives considered**:
- *No ownership check for the POC* — rejected: explicitly required by FR-010
  and flagged as a security concern in the spec's clarification round.
- *Build a full authentication system now* — rejected: explicitly out of scope
  per spec Assumptions.

## 7. Local environment & testing stack

**Decision**: Docker Compose runs a single `pgvector/pgvector` Postgres image
locally; an init script enables the `vector` extension and seeds sample Orders
+ policy/product documents (matching the feature spec's Assumptions). Backend
tests use xUnit + FluentAssertions, with Infrastructure/WebAPI integration
tests running against the same Dockerized Postgres. Frontend tests use Vitest +
React Testing Library.

**Rationale**: Matches the user's explicit Docker Compose direction and keeps
the whole stack (.NET, Postgres, pgvector) reproducible with a single `docker
compose up`, consistent with .NET/React community defaults.

**Alternatives considered**:
- *Testcontainers instead of a static Compose file* — plausible alternative,
  not chosen because the user asked specifically for Docker Compose for local
  Postgres+pgvector; Testcontainers could still be layered on top for CI later
  without contradicting this decision.
