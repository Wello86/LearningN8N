# Contract: Chat API (WebApi ↔ React chat widget)

All endpoints are async, JSON over HTTP. Requests carry the dev-auth stand-in
header `X-Customer-Id` (research.md §6) until a real auth integration replaces
it.

## POST /api/chat/sessions

Starts a new conversation session.

**Request**: no body required.

**Response** `201 Created`:
```json
{
  "sessionId": "5f2c...guid",
  "createdAt": "2026-07-12T10:00:00Z"
}
```

## POST /api/chat/sessions/{sessionId}/messages

Sends one customer message and returns the assistant's synthesized response.
Internally drives one full Reason→Act→Observe loop (research.md §1).

**Request**:
```json
{
  "message": "My order #12345 hasn't arrived yet, it's already 2 days late — what can I do?"
}
```

**Response** `200 OK`:
```json
{
  "turnId": "a91e...guid",
  "message": "I'm sorry about the delay! Order #12345 was expected on Jul 9 and hasn't arrived yet. Our delivery-delay policy allows a partial refund once an order is more than 2 days late, so you do qualify — I can start that for you, or connect you with a support agent if you'd like to discuss it further.",
  "escalateToHuman": false,
  "referencedOrderIds": ["12345"]
}
```

**Response when the guardrail triggers** (`200 OK`, FR-007/FR-011, constitution
Principle VII):
```json
{
  "turnId": "b02f...guid",
  "message": "I can see order #98765, but I don't have a policy on file that covers that situation, so I don't want to guess. I'll connect you with a support agent who can help.",
  "escalateToHuman": true,
  "referencedOrderIds": ["98765"]
}
```

**Errors**:
- `404 Not Found` — unknown `sessionId`.
- `400 Bad Request` — empty/missing `message`.

## GET /api/chat/sessions/{sessionId}/messages

Returns the turn history for a session (used by the widget to restore/replay a
conversation on reload).

**Response** `200 OK`: array of turns, each shaped like the `messages` POST
response above (customer message + assistant response pairs), ordered by
`TurnIndex`.

## Response field notes

- `message` MUST NOT contain internal identifiers other than Order IDs, raw
  SQL/vector data, or tool names (constitution Principle VI) — enforced by the
  prompt contract in `react-tooling.md`, not by the WebApi layer stripping
  text after the fact.
- `escalateToHuman` is the binding signal downstream systems (or the widget)
  act on per constitution Principle VII — never inferred from parsing
  `message` text.
