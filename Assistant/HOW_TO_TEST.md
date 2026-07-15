# How to Test the Shopping Assistant (User Guide)

This is a plain-language walkthrough for trying out the chatbot yourself — what to
type and what a good answer should look like. If you want the technical setup
commands, dependency versions, or how to run automated tests instead, see
`QUICKSTART.md`.

## Before you start

1. Make sure the database is running:
   ```
   docker compose -f docker/docker-compose.yml up -d
   ```
2. Start the backend:
   ```
   dotnet run --project backend/src/AiShoppingAssistant.WebApi
   ```
3. Start the chat app:
   ```
   cd frontend
   npm install
   npm run dev
   ```
4. Open the link it prints — normally **http://localhost:5173** — in your browser.

You're now chatting as a test customer who has 4 orders on file (see below), so you
can ask about any of them.

## Your test orders

| Order # | Item | Status |
|---|---|---|
| 12345 | Wireless Noise-Cancelling Headphones | Delayed |
| 12346 | Stainless Steel Water Bottle | Delivered |
| 12347 | Smart Fitness Tracker | In transit |
| 12348 | Ceramic Non-Stick Frying Pan | Returned |

Order **99999** does not exist — useful for testing the "not found" case.

## What to try

For each one, type the message into the chat and check the answer against the
"Should" column. If it doesn't match, that's worth reporting.

### 1. Checking an order

- Type: **"Where is my order 12345?"**
  Should: say it's **delayed**, and give the dates (ordered / expected delivery) in
  plain English — not just "in progress."
- Type: **"Where is my order 99999?"**
  Should: say it can't find that order — not guess or make something up.

### 2. Asking about store policies or products

- Type: **"What is your return policy?"**
  Should: explain the actual policy (return window, refund timing) — it should read
  like real policy text, not a generic "most stores allow 30 days" guess.
- Type: **"Tell me about the wireless noise-cancelling headphones."**
  Should: describe the real product (noise cancellation, battery life, etc.).

### 3. A question that mixes an order and a policy

- Type: **"My order 12345 seems delayed — am I eligible for a refund under the
  delay policy?"**
  Should: connect the two — confirm the order actually is delayed, then say
  whether the delay policy applies to it, in one combined answer.
- Type: **"Is my order 12346 eligible for a refund under the delay policy?"**
  Should: notice this order was delivered **on time**, so it should say the delay
  policy does **not** apply — not apply it incorrectly just because you asked.

### 4. Asking something outside the chatbot's knowledge

- Type: **"Do you offer free gift wrapping?"** or **"What's the weather like
  today?"** or **"Can I pay with cryptocurrency?"**
  Should: admit it doesn't know, and offer to connect you with a support agent —
  it should never make up an answer or pretend it "checked" something it didn't.

### 5. Following up in the same conversation

- Type: **"Where is my order 12347?"**, wait for the answer, then type:
  **"and can I return it?"**
  Should: understand "it" still means order 12347, without you repeating the
  order number.

## Red flags — things that would mean something's wrong

- It states a policy or product detail that sounds plausible but isn't backed by
  anything specific (a sign it's guessing instead of checking).
- It claims to have "checked" or "looked up" something when the answer doesn't
  actually reflect real data.
- It mentions any internal detail a customer shouldn't see — a customer ID,
  session ID, database ID, similarity score, or error message. The **only** ID it
  should ever say out loud is an order number.
- It applies a policy to an order where it clearly shouldn't (or vice versa).
- It answers confidently on a topic it has no information about, instead of
  saying so and offering to connect you with a person.

If you hit any of these, note the exact message you typed and the exact reply you
got — that's the fastest way to track down the cause.
