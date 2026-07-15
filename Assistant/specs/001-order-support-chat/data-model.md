# Phase 1 Data Model: Order Status & Policy Support Chat

Entities derived from the feature spec's Key Entities, refined with the fields
needed to satisfy FR-001–FR-012 and constitution Principles II/III/VII/VIII.

## Order

Structured record queried via EF Core/Npgsql (`get_order_status` tool).

| Field | Type | Notes |
|---|---|---|
| `OrderId` | string (PK) | Customer-visible identifier (e.g., `12345`); the one internal id customers may see per constitution Principle VI. |
| `CustomerId` | string | Owning customer; used for the FR-010 ownership check. Never exposed in responses. |
| `ProductName` | string | Product ordered. |
| `OrderDate` | date | |
| `DeliveryDate` | date, nullable | Expected or actual delivery date depending on `Status`. |
| `Status` | enum: `InTransit`, `Delivered`, `Delayed`, `Returned` | Drives FR-001/US1 answers. |
| `Amount` | decimal | |

**Validation**: `OrderId` + `CustomerId` together must resolve to at most one
row; lookups filter on both (research.md §6). No order details are returned to
`Core`/the LLM step for a row whose `CustomerId` doesn't match the requester.

## KnowledgeDocument

Unstructured content backing policy and product questions (US2), embedded and
searched via `Pgvector.EntityFrameworkCore` (`search_policy_and_product_docs`
tool).

| Field | Type | Notes |
|---|---|---|
| `DocumentId` | guid (PK) | Internal id — never exposed to the customer (Principle VI). |
| `DocumentType` | enum: `Policy`, `Product` | Distinguishes returns/delivery/warranty policy docs from product descriptions. |
| `Title` | string | e.g., "Returns Policy", "Wireless Headphones — Product Description". |
| `Content` | text | Chunk of policy/product text (short docs, so likely one chunk per document for POC scale). |
| `Embedding` | `vector(1536)` | Generated at seed time per research.md §3. |

**Validation**: Every `KnowledgeDocument` must have a non-null `Embedding`
before it's eligible for retrieval (enforced at seed time, not query time).

## ConversationSession

Represents one customer's chat session; created when the chat widget opens.

| Field | Type | Notes |
|---|---|---|
| `SessionId` | guid (PK) | |
| `CustomerId` | string | From the auth stand-in (research.md §6). |
| `CreatedAt` | timestamp | |

## ConversationTurn

One request/response exchange; doubles as the audit-log record required by
constitution Principle III, and is the source of the "last N turns" context
window for FR-012 (multi-turn memory).

| Field | Type | Notes |
|---|---|---|
| `TurnId` | guid (PK) | |
| `SessionId` | guid (FK → ConversationSession) | |
| `TurnIndex` | int | Ordering within the session. |
| `UserMessage` | text | Raw customer prompt (Principle III audit field). |
| `ToolCallsExecuted` | jsonb | Which tool(s) ran and with what parameters — e.g., `get_order_status(orderId=12345)` (Principle III: "structured queries executed"). |
| `RetrievedSimilarityScores` | jsonb, nullable | Vector similarity score(s) for any `KnowledgeDocument` chunks retrieved this turn (Principle III audit field); null if no vector search ran. |
| `AssistantMessage` | text | Final response text shown to the customer (Principle III audit field). |
| `EscalateToHuman` | boolean | Set per constitution Principle VII / spec FR-011. |
| `CreatedAt` | timestamp | |

**Relationships**: `ConversationSession` 1—* `ConversationTurn`, ordered by
`TurnIndex`. The Reason step (research.md §1) loads the most recent turns for
a session to reconstruct conversation context — kept in a message list
structurally separate from the current turn's `ToolCallsExecuted`/retrieved
data payload, per constitution Principle II.

## State notes

- `Order.Status` and `KnowledgeDocument` content are read-only from this
  feature's perspective (seeded once; no write path is in scope).
- `ConversationSession`/`ConversationTurn` are append-only; no edits or
  deletes are part of this feature's scope.
