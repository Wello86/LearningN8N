# Contract: ReAct tool-calling schema (Core ↔ Claude Sonnet 5 via CodeMie)

This is the internal contract the hand-written ReAct loop (research.md §1)
uses to talk to Claude. It's documented here because it *is* the mechanism
that satisfies constitution Principle I (hybrid routing) and Principle II
(context isolation) — get this contract wrong and both principles fail
silently at runtime.

## Tool definitions offered to Claude every turn

### `get_order_status`

```json
{
  "name": "get_order_status",
  "description": "Look up the current status, dates, and amount for one of the requesting customer's own orders by order id. Returns not-found if the order id doesn't exist or doesn't belong to this customer.",
  "input_schema": {
    "type": "object",
    "properties": {
      "orderId": { "type": "string" }
    },
    "required": ["orderId"]
  }
}
```

### `search_policy_and_product_docs`

```json
{
  "name": "search_policy_and_product_docs",
  "description": "Search store policy documents (returns, delivery/delay, warranty) and product descriptions for content relevant to a customer question.",
  "input_schema": {
    "type": "object",
    "properties": {
      "query": { "type": "string" }
    },
    "required": ["query"]
  }
}
```

## Message structure (context isolation, Principle II)

Every Reason-step call to Claude is assembled as:

1. **System prompt** — fixed: assistant persona/tone rules (constitution
   Principle VI), the two tool definitions above, and the instruction to emit
   the sentinel `[LOW_CONFIDENCE]` marker instead of guessing when it cannot
   answer from tool results (research.md §4).
2. **Conversation history block** — the prior turns' `UserMessage` /
   `AssistantMessage` pairs only (from `ConversationTurn`, data-model.md), with
   no tool-result payloads mixed in.
3. **Current turn** — the new `UserMessage`.
4. **Retrieved data block** (only present after an Act step) — the typed tool
   result(s) (`OrderRecord`, `PolicyChunk[]`) serialized into a clearly labeled
   section, e.g. a message tagged `role: "tool_result"` per Claude's native
   tool-use format — never appended into the conversation history block.

Steps 2 and 4 MUST remain distinct message entries/sections; the Core prompt
builder MUST NOT concatenate retrieved data into free-form history text.

## Loop termination contract

- Claude requests one or more tool calls → loop executes them (Act), appends
  results as a new "retrieved data" block (Observe), and calls Claude again.
- Claude returns plain text with no tool call → that text is the candidate
  final answer.
- If the candidate final answer contains `[LOW_CONFIDENCE]`, OR a tool call
  returned empty/not-found, the WebApi layer discards the raw text and
  substitutes the standard fallback message, setting `escalateToHuman: true`
  (constitution Principle VII) — the sentinel token itself MUST NOT leak into
  the customer-visible `message` field.
- Loop iteration cap: 3 Reason→Act→Observe cycles; hitting the cap without a
  final answer is itself treated as a guardrail trigger (fallback +
  `escalateToHuman: true`).
