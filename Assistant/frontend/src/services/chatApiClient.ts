/**
 * Typed REST client for the chat API described in
 * specs/001-order-support-chat/contracts/chat-api.md.
 *
 * Endpoints:
 *  - POST /api/chat/sessions                        -> createSession
 *  - GET  /api/chat/sessions/{sessionId}/messages    -> getSessionMessages
 *  - POST /api/chat/sessions/{sessionId}/messages    -> sendMessage
 */

// Dev-auth stand-in (research.md §6): the real backend has no authentication
// yet, so every request carries a hardcoded customer id via the
// `X-Customer-Id` header. This is a placeholder for the POC only and must be
// replaced once real session/auth integration lands. Matches the seeded
// customer in docker/init/02-seed-sample-data.sql so the widget can resolve
// real orders.
const DEV_CUSTOMER_ID = 'cust-1001';

const API_BASE_URL = '/api/chat';

/** Response shape for POST /api/chat/sessions. */
export interface CreateSessionResponse {
  sessionId: string;
  createdAt: string;
}

/**
 * Response shape for POST /api/chat/sessions/{sessionId}/messages, per
 * contracts/chat-api.md (also the guardrail-triggered shape, which just sets
 * `escalateToHuman: true`).
 */
export interface ChatTurnResponse {
  turnId: string;
  message: string;
  escalateToHuman: boolean;
  referencedOrderIds: string[];
}

/**
 * One entry of GET /api/chat/sessions/{sessionId}/messages history.
 *
 * contracts/chat-api.md describes each history entry as "customer message +
 * assistant response pairs" shaped like the POST response; since the POST
 * response itself carries only the assistant side, this type adds the
 * `userMessage` field (mirroring data-model.md's `ConversationTurn.UserMessage`)
 * needed to reconstruct the full exchange on reload.
 */
export interface ChatHistoryTurn extends ChatTurnResponse {
  userMessage: string;
}

/** Request body for POST /api/chat/sessions/{sessionId}/messages. */
interface SendMessageRequest {
  message: string;
}

/** Thrown for any non-2xx response from the chat API. */
export class ChatApiError extends Error {
  readonly status: number;

  constructor(message: string, status: number) {
    super(message);
    this.name = 'ChatApiError';
    this.status = status;
  }
}

function buildHeaders(hasBody: boolean): HeadersInit {
  const headers: Record<string, string> = {
    'X-Customer-Id': DEV_CUSTOMER_ID,
  };
  if (hasBody) {
    headers['Content-Type'] = 'application/json';
  }
  return headers;
}

async function parseJsonOrThrow<T>(response: Response): Promise<T> {
  if (!response.ok) {
    let detail = '';
    try {
      detail = await response.text();
    } catch {
      // Ignore failures reading the error body; fall back to the status text.
    }
    throw new ChatApiError(
      `Chat API request failed with status ${response.status}${detail ? `: ${detail}` : ''}`,
      response.status,
    );
  }
  return (await response.json()) as T;
}

/** POST /api/chat/sessions — starts a new conversation session. */
export async function createSession(): Promise<CreateSessionResponse> {
  const response = await fetch(`${API_BASE_URL}/sessions`, {
    method: 'POST',
    headers: buildHeaders(false),
  });
  return parseJsonOrThrow<CreateSessionResponse>(response);
}

/**
 * GET /api/chat/sessions/{sessionId}/messages — returns the turn history for
 * a session, ordered by TurnIndex, used to restore/replay a conversation.
 */
export async function getSessionMessages(
  sessionId: string,
): Promise<ChatHistoryTurn[]> {
  const response = await fetch(
    `${API_BASE_URL}/sessions/${sessionId}/messages`,
    {
      method: 'GET',
      headers: buildHeaders(false),
    },
  );
  return parseJsonOrThrow<ChatHistoryTurn[]>(response);
}

/**
 * POST /api/chat/sessions/{sessionId}/messages — sends one customer message
 * and returns the assistant's synthesized response for that turn.
 */
export async function sendMessage(
  sessionId: string,
  message: string,
): Promise<ChatTurnResponse> {
  const body: SendMessageRequest = { message };
  const response = await fetch(
    `${API_BASE_URL}/sessions/${sessionId}/messages`,
    {
      method: 'POST',
      headers: buildHeaders(true),
      body: JSON.stringify(body),
    },
  );
  return parseJsonOrThrow<ChatTurnResponse>(response);
}
