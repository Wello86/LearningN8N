# Quickstart: Run & Test

Practical steps to run the app locally and run its test suites. For scenario-by-scenario
feature validation (US1/US2/US3, guardrail, audit logging, multi-turn memory), see
`specs/001-order-support-chat/quickstart.md`.

## Prerequisites

- .NET 10 SDK
- Node.js + npm
- Docker Desktop running

## 1. Start the database

```bash
docker compose -f docker/docker-compose.yml up -d
```

This starts a `pgvector/pgvector` Postgres container and runs `docker/init/*.sql`, which
enables the `vector` extension and seeds sample orders + policy/product documents.

Verify it's healthy:

```bash
docker ps --filter "name=ai-shopping-assistant-postgres"
```

The same compose file also starts a **pgAdmin** container for browsing the database
(tables, rows, the `knowledge_documents.Embedding` vectors, etc.) without the psql CLI:

- URL: http://localhost:5050
- Login: `admin@admin.com` / `admin`
- Register a server (first time only) with host `postgres`, port `5432`, database
  `ai_shopping_assistant`, user/password `ai_shopping_assistant`

## 2. (Optional) Apply EF Core migrations

Usually not needed — the seed scripts handle it — but if you change entities:

```bash
dotnet ef database update --project backend/src/AiShoppingAssistant.Infrastructure
```

## 3. Run the backend

```bash
dotnet run --project backend/src/AiShoppingAssistant.WebApi
```

`backend/src/AiShoppingAssistant.WebApi/appsettings.json` has empty `CodeMie:BaseUrl` /
`CodeMie:ApiKey`. Without real credentials there, live chat calls to Claude will fail —
that's why all backend tests swap `IChatModel`/`IEmbeddingModel` for scripted doubles
instead of hitting CodeMie. If you have a real CodeMie gateway key, set it via
`dotnet user-secrets` or environment variables (`CodeMie__ApiKey`, `CodeMie__BaseUrl`)
before running live.

## 4. Run the frontend

```bash
cd frontend
npm install
npm run dev
```

Opens the chat widget dev server (Vite). It talks to the backend via
`src/services/chatApiClient.ts`, sending a dev-auth stand-in header
`X-Customer-Id: cust-1001` (matches the seeded orders in
`docker/init/02-seed-sample-data.sql`).

## 5. Try it manually

With both backend and frontend running, in the chat widget:

- **US1 — order status**: "Where is my order 12345?" → should mention it's delayed.
  "Where is my order 99999?" → fallback + escalation.
- **US2 — policy/product**: "What is your return policy?" → grounded answer. Ask about an
  unseeded topic → fallback + escalation.
- **US3 — combined**: "My order 12345 is late — what's your delay policy, and does it
  apply?" → one combined answer.
- **Multi-turn memory**: "Where is my order 12346?" then "and can I get a refund for
  that?" → resolves without repeating the order id.

This needs a live CodeMie key to actually reason/call tools. Without one, use the
automated tests below instead, which substitute scripted chat/embedding doubles.

## 6. Run the tests

**Backend unit tests** (no Docker needed):

```bash
dotnet test backend/tests/AiShoppingAssistant.Core.Tests
dotnet test backend/tests/AiShoppingAssistant.Infrastructure.Tests
```

**Backend integration tests** (needs the Postgres container from step 1 running):

```bash
dotnet test backend/tests/AiShoppingAssistant.WebApi.IntegrationTests
```

**Frontend tests**:

```bash
cd frontend
npm test -- --run
```

**Everything in the backend at once** (unit + integration):

```bash
dotnet test backend
```

> No end-to-end (Playwright/Cypress-style) suite exists yet — only unit and integration
> tests, plus the manual scenario walkthrough in step 5.

## 7. Tear down

```bash
docker compose -f docker/docker-compose.yml down
```

Add `-v` to also drop the seeded data volume for a clean slate next time.
