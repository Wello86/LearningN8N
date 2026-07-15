---

description: "Task list for Order Status & Policy Support Chat"
---

# Tasks: Order Status & Policy Support Chat

**Input**: Design documents from `/specs/001-order-support-chat/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/chat-api.md, contracts/react-tooling.md, quickstart.md, `.specify/memory/constitution.md`

**Tests**: Included. The constitution's Development & Review Workflow requires the hallucination guardrail / `EscalateToHuman` flag to be "exercised by a test for any new query/response path" and treats Principles I, III, and VII as release blockers, so contract/integration tests are part of each user story below (not a separate opt-in TDD pass).

**Organization**: Tasks are grouped by user story (US1/US2/US3, from spec.md) to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- File paths are relative to the repository root

## Path Conventions

Per plan.md's Project Structure: `backend/src/AiShoppingAssistant.{WebApi,Core,Infrastructure}`, `backend/tests/{Core,Infrastructure,WebApi.Integration}Tests`, `frontend/src/{components,pages,services}`, `frontend/tests/`, `docker/`.

---

## Phase 1: Setup

**Purpose**: Project initialization and basic structure

- [X] T001 Create the backend solution and three projects (`AiShoppingAssistant.WebApi`, `AiShoppingAssistant.Core`, `AiShoppingAssistant.Infrastructure`) plus three test projects (`AiShoppingAssistant.Core.Tests`, `AiShoppingAssistant.Infrastructure.Tests`, `AiShoppingAssistant.WebApi.IntegrationTests`) under `backend/`, wired per constitution Principle IV (WebApi/Infrastructure depend on Core; Core depends on neither)
- [X] T002 [P] Scaffold the React SPA in `frontend/` (`src/components/`, `src/pages/`, `src/services/`, `tests/`) with Vite + TypeScript + Vitest/React Testing Library per plan.md Technical Context
- [X] T003 [P] Create `docker/docker-compose.yml` running the `pgvector/pgvector` Postgres image for local dev/integration tests
- [X] T004 [P] Create `docker/init/01-enable-pgvector.sql` enabling the `vector` extension on startup
- [X] T005 [P] Configure backend linting/formatting (`.editorconfig`, nullable + warnings-as-errors) and frontend linting/formatting (ESLint + Prettier)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Infrastructure shared by all three user stories — audit logging, the chat transport, and the generic ReAct loop shell (no tools registered yet)

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T006 Add EF Core + Npgsql + `Pgvector.EntityFrameworkCore` package references and configure `AiShoppingAssistantDbContext` in `backend/src/AiShoppingAssistant.Infrastructure/AiShoppingAssistantDbContext.cs`
- [X] T007 [P] Create the `ConversationSession` entity (`SessionId`, `CustomerId`, `CreatedAt`) in `backend/src/AiShoppingAssistant.Core/Entities/ConversationSession.cs`
- [X] T008 [P] Create the `ConversationTurn` entity (`TurnId`, `SessionId`, `TurnIndex`, `UserMessage`, `ToolCallsExecuted` jsonb, `RetrievedSimilarityScores` jsonb nullable, `AssistantMessage`, `EscalateToHuman`, `CreatedAt`) in `backend/src/AiShoppingAssistant.Core/Entities/ConversationTurn.cs` — doubles as the constitution Principle III audit log
- [X] T009 Create the initial EF Core migration for `ConversationSession`/`ConversationTurn` in `backend/src/AiShoppingAssistant.Infrastructure/Migrations/` (depends on T006, T007, T008) — migration generated; not yet applied to a live DB pending Docker Desktop startup
- [X] T010 [P] Implement the dev-auth stand-in middleware reading the `X-Customer-Id` header (research.md §6) in `backend/src/AiShoppingAssistant.WebApi/Middleware/DevCustomerIdMiddleware.cs`
- [X] T011 [P] Define the `IChatModel` port (`SendAsync(ChatRequest) -> ChatCompletionResult`) in `backend/src/AiShoppingAssistant.Core/Ports/IChatModel.cs`
- [X] T012 Implement `ICodeMieChatClient` against EPAM's CodeMie gateway (Claude Sonnet 5, native tool-use format) in `backend/src/AiShoppingAssistant.Infrastructure/CodeMie/CodeMieChatClient.cs` (depends on T011)
- [X] T013 Implement the generic Reason→Act→Observe loop — system prompt / conversation-history block / current-turn / retrieved-data block kept as distinct message sections per constitution Principle II, plus the 3-iteration cap — with no tools registered yet, in `backend/src/AiShoppingAssistant.Core/ReAct/ReActLoop.cs` (depends on T011)
- [X] T014 Implement audit-log persistence of each completed loop run into `ConversationTurn` (`UserMessage`, `ToolCallsExecuted`, `RetrievedSimilarityScores`, `AssistantMessage`, `EscalateToHuman`) in `backend/src/AiShoppingAssistant.Infrastructure/Repositories/ConversationRepository.cs` (depends on T008, T009)
- [X] T015 Implement `POST /api/chat/sessions` and `GET /api/chat/sessions/{sessionId}/messages` per contracts/chat-api.md in `backend/src/AiShoppingAssistant.WebApi/Controllers/ChatController.cs` (depends on T007, T014)
- [X] T016 Implement `POST /api/chat/sessions/{sessionId}/messages` wiring the ReAct loop (T013) and audit logging (T014), returning the standard fallback since no tools are registered yet, in `backend/src/AiShoppingAssistant.WebApi/Controllers/ChatController.cs` (depends on T013, T014, T015)
- [X] T017 [P] Implement `GuardrailPolicy` — the `[LOW_CONFIDENCE]` sentinel / empty-tool-result / iteration-cap short-circuit into the fallback message + `EscalateToHuman: true`, per contracts/react-tooling.md's Loop termination contract — in `backend/src/AiShoppingAssistant.Core/ReAct/GuardrailPolicy.cs` (depends on T013)
- [X] T018 [P] Build the chat widget shell (`ChatWidget`, `MessageList`, `MessageBubble`, `EscalationBanner` in `frontend/src/components/`, `ChatPage` in `frontend/src/pages/`, `chatApiClient` in `frontend/src/services/`) calling the endpoints from T015/T016 (depends on T002)

**Checkpoint**: Foundation ready — user story implementation can now begin

---

## Phase 3: User Story 1 - Check Order Status (Priority: P1) 🎯 MVP

**Goal**: A customer can ask about a specific order by its order identifier and get its real status/dates, or a "can't find that order" fallback.

**Independent Test**: Ask about an order by order identifier and confirm the response reflects that order's actual status/dates (including the delayed case) using only structured order data, with a not-found fallback for a nonexistent id.

### Tests for User Story 1

- [X] T019 [P] [US1] Integration test covering spec Acceptance Scenarios 1–3 (valid order status, delayed order, unknown order → fallback with `escalateToHuman: true`) in `backend/tests/AiShoppingAssistant.WebApi.IntegrationTests/OrderStatusChatTests.cs`
- [X] T020 [P] [US1] Unit test proving `get_order_status` filters by `OrderId` **and** `CustomerId` and returns not-found (not forbidden) for another customer's order (FR-010) in `backend/tests/AiShoppingAssistant.Core.Tests/OrderLookupTests.cs`

### Implementation for User Story 1

- [X] T021 [P] [US1] Create the `Order` entity (`OrderId`, `CustomerId`, `ProductName`, `OrderDate`, `DeliveryDate`, `Status` enum, `Amount`) in `backend/src/AiShoppingAssistant.Core/Entities/Order.cs`
- [X] T022 [US1] Add the `Order` EF Core mapping and migration in `backend/src/AiShoppingAssistant.Infrastructure/Migrations/` (depends on T006, T021)
- [X] T023 [US1] Create `docker/init/02-seed-sample-data.sql` seeding ~5–10 sample orders covering every `Status` value (depends on T022)
- [X] T024 [US1] Define the `IOrderLookup` port in `backend/src/AiShoppingAssistant.Core/Ports/IOrderLookup.cs`
- [X] T025 [US1] Implement `OrderRepository` (EF Core/Npgsql, filters on `OrderId` + `CustomerId`, returns not-found rather than forbidden per research.md §6) in `backend/src/AiShoppingAssistant.Infrastructure/Repositories/OrderRepository.cs` (depends on T021, T022, T024)
- [X] T026 [US1] Register the `get_order_status` tool definition (contracts/react-tooling.md) and wire `IOrderLookup` into the loop's Act step in `backend/src/AiShoppingAssistant.Core/ReAct/Tools/GetOrderStatusTool.cs` (depends on T013, T024, T025)
- [X] T027 [US1] Extend the system prompt with order-lookup tone rules (Order ID is the one exposable id; no other internal ids) in `backend/src/AiShoppingAssistant.Core/ReAct/SystemPromptBuilder.cs` (depends on T026)
- [X] T028 [US1] Wire order-not-found tool results into `GuardrailPolicy`'s fallback + `EscalateToHuman: true` in `backend/src/AiShoppingAssistant.Core/ReAct/GuardrailPolicy.cs` (depends on T017, T026)
- [X] T029 [US1] Render order-status responses (including the escalation banner) in `frontend/src/components/ChatWidget.tsx` (depends on T018)

**Checkpoint**: User Story 1 is fully functional and independently testable/demoable

---

## Phase 4: User Story 2 - Ask a Policy or Product Question (Priority: P2)

**Goal**: A customer can ask a store-policy or product-attribute question and get an answer grounded in the real policy/product documents, or a "can't confidently answer" fallback.

**Independent Test**: Ask a policy/product question and confirm the response is grounded in actual document content, with a fallback (no invented answer) when no document covers the topic — no order data involved.

### Tests for User Story 2

- [X] T030 [P] [US2] Integration test covering spec Acceptance Scenarios 1–3 (returns-policy answer, product-attribute answer, uncovered topic → fallback with `escalateToHuman: true`) in `backend/tests/AiShoppingAssistant.WebApi.IntegrationTests/PolicyProductChatTests.cs`
- [X] T031 [P] [US2] Unit test proving `search_policy_and_product_docs` triggers the guardrail when no chunk clears the similarity threshold (research.md §4) in `backend/tests/AiShoppingAssistant.Core.Tests/PolicySearchTests.cs`

### Implementation for User Story 2

- [X] T032 [P] [US2] Create the `KnowledgeDocument` entity (`DocumentId`, `DocumentType` enum Policy/Product, `Title`, `Content`, `Embedding` vector(1536)) in `backend/src/AiShoppingAssistant.Core/Entities/KnowledgeDocument.cs`
- [X] T033 [US2] Add the `KnowledgeDocument` EF Core + `Pgvector.EntityFrameworkCore` mapping and migration in `backend/src/AiShoppingAssistant.Infrastructure/Migrations/` (depends on T006, T032)
- [X] T034 [US2] Implement the CodeMie embedding client used at seed time (research.md §3) in `backend/src/AiShoppingAssistant.Infrastructure/CodeMie/CodeMieEmbeddingClient.cs` (depends on T012)
- [X] T035 [US2] Extend `docker/init/02-seed-sample-data.sql` with sample policy (returns, delivery-delay, warranty) and product documents plus their pre-computed embeddings (depends on T033, T034)
- [X] T036 [US2] Define the `IPolicySearch` port in `backend/src/AiShoppingAssistant.Core/Ports/IPolicySearch.cs`
- [X] T037 [US2] Implement `KnowledgeDocumentRepository` (pgvector cosine-similarity search against a fixed threshold) in `backend/src/AiShoppingAssistant.Infrastructure/Repositories/KnowledgeDocumentRepository.cs` (depends on T032, T033, T036)
- [X] T038 [US2] Register the `search_policy_and_product_docs` tool definition and wire `IPolicySearch` into the loop's Act step in `backend/src/AiShoppingAssistant.Core/ReAct/Tools/SearchPolicyAndProductDocsTool.cs` (depends on T013, T036, T037)
- [X] T039 [US2] Wire empty/low-similarity search results into `GuardrailPolicy`'s fallback + `EscalateToHuman: true` in `backend/src/AiShoppingAssistant.Core/ReAct/GuardrailPolicy.cs` (depends on T017, T038)
- [X] T040 [US2] Render policy/product responses (including the escalation banner) in `frontend/src/components/ChatWidget.tsx` (depends on T018, T029)

**Checkpoint**: User Stories 1 AND 2 both work independently

---

## Phase 5: User Story 3 - Ask a Combined Order + Policy Question (Priority: P3)

**Goal**: A customer can ask one question spanning a specific order and a general policy and get one synthesized answer connecting both.

**Independent Test**: Ask a question referencing both a specific order and a policy topic and confirm the single response correctly combines that order's real data with the real policy content (including the on-time-order negative case and the partial-confidence case).

### Tests for User Story 3

- [X] T041 [P] [US3] Integration test: combined question against a delayed order whose delay policy applies (Acceptance Scenario 1) in `backend/tests/AiShoppingAssistant.WebApi.IntegrationTests/CombinedChatTests.cs`
- [X] T042 [P] [US3] Integration test: combined question against an on-time order where the delay policy does not apply (Acceptance Scenario 2) in `backend/tests/AiShoppingAssistant.WebApi.IntegrationTests/CombinedChatTests.cs`
- [X] T043 [P] [US3] Integration test: partial-confidence combined question — order resolves, no matching policy exists — answers the resolvable part and flags the rest instead of guessing (Acceptance Scenario 3) in `backend/tests/AiShoppingAssistant.WebApi.IntegrationTests/CombinedChatTests.cs`

### Implementation for User Story 3

- [X] T044 [US3] Extend the system prompt to instruct Claude it may call both `get_order_status` and `search_policy_and_product_docs` within the same turn and synthesize one combined answer in `backend/src/AiShoppingAssistant.Core/ReAct/SystemPromptBuilder.cs` (depends on T027, T038)
- [X] T045 [US3] Extend `GuardrailPolicy` to support partial-confidence answers — state the confidently-resolved part while flagging only the unresolved part, without discarding the resolvable part — in `backend/src/AiShoppingAssistant.Core/ReAct/GuardrailPolicy.cs` (depends on T028, T039)
- [X] T046 [US3] Load the last 10 `ConversationTurn` rows into the history block so a combined follow-up resolves against an order/topic mentioned earlier in the session (FR-012) in `backend/src/AiShoppingAssistant.Core/ReAct/ConversationHistoryLoader.cs` (depends on T014)
- [X] T047 [US3] Render combined-answer responses (order + policy content in one message bubble) in `frontend/src/components/ChatWidget.tsx` (depends on T029, T040)

**Checkpoint**: All three user stories are independently functional

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T048 [P] Frontend component tests (Vitest + React Testing Library) for `ChatWidget`/`EscalationBanner` in `frontend/tests/ChatWidget.test.tsx`
- [X] T049 [P] Code-review pass verifying fully asynchronous I/O end-to-end with no blocking calls (constitution Principle V) across `backend/src/`
- [X] T050 [P] Code-review pass verifying no untyped payloads cross into prompt construction (constitution Principle VIII) in `backend/src/AiShoppingAssistant.Core/ReAct/`
- [X] T051 Run all quickstart.md validation scenarios (US1/US2/US3, guardrail check, audit logging check, multi-turn memory check) end-to-end against the Dockerized stack

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories
- **User Story 1 (Phase 3)**: Depends only on Foundational
- **User Story 2 (Phase 4)**: Depends only on Foundational (independent of US1's Order-specific code; T040 touches the same `ChatWidget.tsx` file as T029, so run US1's T029 first if both are in flight)
- **User Story 3 (Phase 5)**: Depends on Foundational **and** on both US1 (T027, T028) and US2 (T038, T039) being implemented, since it synthesizes both tools' results into one answer
- **Polish (Phase 6)**: Depends on all desired user stories being complete

### Parallel Opportunities

- All Setup tasks marked [P] (T002–T005) can run in parallel
- Within Foundational: T007/T008 in parallel; T010/T011 in parallel; T017 in parallel with T015/T016 once T013 lands
- Once Foundational is done, US1 (Phase 3) and US2 (Phase 4) can be staffed and implemented in parallel — they touch disjoint entities/ports/tools and only share the append-only `ConversationTurn` audit path
- US3 (Phase 5) cannot start until both US1 and US2 have registered their tools
- All tests within a story marked [P] can run in parallel; all [P] entity/port creation tasks within a story can run in parallel

---

## Parallel Example: User Story 1

```bash
# Tests together:
Task: "Integration test covering order-status Acceptance Scenarios 1-3 in backend/tests/AiShoppingAssistant.WebApi.IntegrationTests/OrderStatusChatTests.cs"
Task: "Unit test for get_order_status ownership filtering in backend/tests/AiShoppingAssistant.Core.Tests/OrderLookupTests.cs"

# Entity creation:
Task: "Create Order entity in backend/src/AiShoppingAssistant.Core/Entities/Order.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (blocks all stories)
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: run the US1 quickstart scenario independently
5. Deploy/demo if ready

### Incremental Delivery

1. Setup + Foundational → foundation ready
2. Add User Story 1 → validate → demo (MVP)
3. Add User Story 2 → validate → demo
4. Add User Story 3 (needs US1 + US2 tool wiring) → validate → demo
5. Polish

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps a task to its user story for traceability
- Guardrail/`EscalateToHuman` behavior is release-blocking per the constitution — do not skip T028/T039/T045 or their tests
- Commit after each task or logical group; stop at each checkpoint to validate a story independently
