# Quickstart: Order Status & Policy Support Chat

Validates the feature end-to-end against the three user stories and the
hallucination guardrail. See `data-model.md` for entity shapes and
`contracts/` for the API and tool-calling contracts referenced below.

## Prerequisites

- .NET 10 SDK
- Node.js (for the React chat widget)
- Docker (for Postgres + pgvector via Docker Compose)
- A CodeMie gateway base URL + API key with access to Claude Sonnet 5 (and its
  embedding endpoint, per `research.md` §3) — this is an EPAM-internal
  credential (see Setup step 3 below for where it goes); it is **not**
  available from any public package or endpoint. There is no self-service
  token endpoint for this — obtain the API key through EPAM's own CodeMie
  access channel (internal portal/onboarding, or a teammate with existing
  access) and paste the resulting bearer token as-is; do not look for or
  script an OAuth/password-grant flow, there isn't one to find in this repo.

## Setup

1. Start the database:
   ```
   docker compose -f docker/docker-compose.yml up -d
   ```
   This provisions Postgres with the `vector` extension enabled and seeds the
   sample Orders + policy/product documents (`docker/init/*.sql`). The seeded
   `KnowledgeDocument` rows carry real embeddings (computed against the live
   CodeMie embedding endpoint, not placeholder vectors), and their `Content`
   is written in an Allegro.pl/Amazon.pl-style voice — so US2/US3 below should
   return real, confident answers out of the box rather than guardrail
   fallbacks.

2. Apply EF Core migrations (if not already applied by the init scripts):
   ```
   dotnet ef database update --project backend/src/AiShoppingAssistant.Infrastructure
   ```

3. Configure the CodeMie gateway credentials. `appsettings.Development.json`
   ships with `CodeMie:BaseUrl`/`CodeMie:ApiKey` blank on purpose — no live
   credentials are committed to this repo (see the doc comment on
   `CodeMieOptions`). Without them, `CodeMieChatClient`/`CodeMieEmbeddingClient`
   throw `CodeMie:BaseUrl is not configured` as soon as a chat message is sent.
   Get your EPAM CodeMie gateway base URL + API key (internal EPAM
   onboarding/portal, or ask a teammate with existing access — this is not a
   public package or endpoint; there is no token/OAuth endpoint documented
   anywhere in this repo, so don't go looking for one), then set them via
   `dotnet user-secrets` (already initialized for `AiShoppingAssistant.WebApi`,
   so real keys never land in a tracked config file):
   ```
   cd backend/src/AiShoppingAssistant.WebApi
   dotnet user-secrets set "CodeMie:BaseUrl" "<your gateway base url>"
   dotnet user-secrets set "CodeMie:ApiKey" "<your api key>"
   ```
   A few gotchas confirmed while getting this feature working end-to-end
   against a real CodeMie gateway:
   - `BaseUrl` **must have a trailing slash** (e.g.
     `https://codemie.<host>/code-assistant-api/v1/`) — `CodeMieChatClient`/
     `CodeMieEmbeddingClient` append relative paths like `chat/completions` and
     `embeddings` directly onto it, so a missing trailing slash silently
     produces a 404. The doc-comment example on `CodeMieOptions.BaseUrl`
     omits the trailing slash — don't copy that literally.
   - `ApiKey` is a long JWT-like bearer token (over a thousand characters),
     not a short static API key — paste it in full as one value.
   - Set `CodeMie:Model` to `claude-sonnet-5` and `CodeMie:EmbeddingModel` to
     `codemie-text-embedding-ada-002` — these are the model ids that actually
     work against the gateway (and match `appsettings.Development.json`).
     The non-Development `appsettings.json` currently ships different,
     unverified values (`anthropic.claude-sonnet-5` / `voyage-2`); if you hit
     model-not-found errors outside the `Development` environment, override
     `CodeMie:Model`/`CodeMie:EmbeddingModel` via user secrets or environment
     variables the same way as `BaseUrl`/`ApiKey`.
   - If the gateway starts returning an HTML "blocked" page instead of JSON,
     you're being caught by a Cloudflare WAF rule on the User-Agent header —
     this only affects hand-rolled HTTP clients used for manual testing (e.g.
     Python's `urllib`), not the app itself, which uses `HttpClient`.

   User secrets override `appsettings.Development.json` automatically when
   running in the `Development` environment (the default for `dotnet run`).
   Equivalently, you can export environment variables instead of user secrets
   — ASP.NET Core's default configuration reads these too, using `__` for
   nesting: `CodeMie__BaseUrl`, `CodeMie__ApiKey`.

4. Run the backend:
   ```
   dotnet run --project backend/src/AiShoppingAssistant.WebApi
   ```

5. Run the frontend:
   ```
   cd frontend && npm install && npm run dev
   ```
   Open the URL Vite prints (e.g. `http://localhost:5173`) — that's the chat
   widget (`ChatPage`/`ChatWidget`), not the backend. Vite's dev server proxies
   `/api/**` to the backend at `http://localhost:5228` (see
   `frontend/vite.config.ts`), so the widget's requests reach the WebApi
   without a CORS setup.

## A note on the dev-auth stand-in header

Every backend request requires an `X-Customer-Id` header (research.md §6) —
there is no real authentication yet. This means:

- The backend has no browsable root route. Opening
  `http://localhost:5228/` directly in a browser returns `401` with
  `Missing required 'X-Customer-Id' header ...` — this is expected, not a bug.
  Use the chat widget (which sets the header automatically via
  `frontend/src/services/chatApiClient.ts`) or a tool that lets you set custom
  headers (curl, Postman, the `AiShoppingAssistant.WebApi.http` file) instead.
- To call the API directly per `contracts/chat-api.md`, include the header,
  e.g.:
  ```
  curl -X POST http://localhost:5228/api/chat/sessions -H "X-Customer-Id: cust-1001"
  ```
- The frontend widget always sends a hardcoded `cust-1001` (see
  `DEV_CUSTOMER_ID` in `chatApiClient.ts`), matching the first seeded customer
  in `docker/init/02-seed-sample-data.sql` (orders `12345`–`12348`). If you
  call the API directly with a different customer id (e.g. `cust-1002`, orders
  `20001`–`20004`), use that customer's own order ids — `get_order_status`
  filters by order id **and** customer id (FR-010) and returns "not found" for
  another customer's order.

## Validation scenarios

For each scenario: open the chat widget (or call the API directly per
`contracts/chat-api.md`, remembering the `X-Customer-Id` header above), start
a session, and send the message using a seeded sample customer/order id from
`docker/init/02-seed-sample-data.sql`.

### US1 — Order status (P1)

- Send: `"Where is my order #<seeded-delayed-order-id>?"`
- **Expect**: response states the order's actual status/date from the seeded
  data (e.g., reflects "delayed"), `escalateToHuman: false`.
- Repeat with a non-existent order id → expect a "can't find that order"
  fallback with `escalateToHuman: true`.

### US2 — Policy/product question (P2)

- Send: `"What is your return policy?"`
- **Expect**: response reflects the actual seeded returns-policy content
  (the 14-day statutory withdrawal right plus the extended returns window —
  see `docker/init/02-seed-sample-data.sql`), `escalateToHuman: false`.
- Send a question with no matching seeded document (e.g., about an unseeded
  policy topic) → expect the fallback response with `escalateToHuman: true`.

### US3 — Combined order + policy question (P3)

- Send: `"My order #<seeded-delayed-order-id> is late — what's your delay
  policy, and does it apply to my order?"`
- **Expect**: one response combining the order's actual delay status with the
  actual delay-policy content, explicitly stating whether it applies,
  `escalateToHuman: false`.
- Repeat referencing a seeded **on-time** order → expect the response to
  correctly state the delay policy does not apply.

### Guardrail check (constitution Principle VII / SC-004)

- Ask an unrelated or unanswerable question (e.g., about a topic with no
  seeded document and no order reference).
- **Expect**: polite "can't confidently answer" message, `escalateToHuman:
  true`, and no invented policy/order details in `message`.

### Audit logging check (constitution Principle III)

- After running the above scenarios, query the `ConversationTurn` table for
  the session used.
- **Expect**: each row has a non-null `UserMessage`, `ToolCallsExecuted`,
  `AssistantMessage`, and (for US2/US3 turns) `RetrievedSimilarityScores`.

### Multi-turn memory check (FR-012)

- Send `"Where is my order #<seeded-order-id>?"`, then follow up in the same
  session with `"and can I get a refund for that?"` (no order id repeated).
- **Expect**: the follow-up response resolves against the order mentioned in
  the prior turn without asking the customer to repeat the order id.

## Success signal

All scenarios above match their expected behavior — this corresponds to
SC-001 through SC-005 in `spec.md` being satisfied for the sampled cases.
