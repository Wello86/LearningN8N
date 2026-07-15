import { useCallback, useEffect, useRef, useState } from 'react';
import MessageList from './MessageList';
import type { ChatDisplayMessage } from './MessageBubble';
import {
  ChatApiError,
  createSession,
  getSessionMessages,
  sendMessage,
} from '../services/chatApiClient';
import type { ChatHistoryTurn } from '../services/chatApiClient';

function historyTurnToDisplayMessages(
  turn: ChatHistoryTurn,
): ChatDisplayMessage[] {
  return [
    { id: `${turn.turnId}-user`, sender: 'user', text: turn.userMessage },
    {
      id: `${turn.turnId}-assistant`,
      sender: 'assistant',
      text: turn.message,
      escalateToHuman: turn.escalateToHuman,
      referencedOrderIds: turn.referencedOrderIds,
    },
  ];
}

function describeError(err: unknown, fallback: string): string {
  return err instanceof ChatApiError ? `${fallback}: ${err.message}` : fallback;
}

/**
 * Stateful chat widget: creates a session, loads history, and lets the
 * customer send messages and see the assistant's replies (including the
 * escalation banner when a reply's `escalateToHuman` flag is set).
 */
function ChatWidget() {
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [messages, setMessages] = useState<ChatDisplayMessage[]>([]);
  const [draft, setDraft] = useState('');
  const [isInitializing, setIsInitializing] = useState(true);
  const [isSending, setIsSending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const messagesEndRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    let isCancelled = false;

    async function initializeSession() {
      setIsInitializing(true);
      setError(null);
      try {
        const session = await createSession();
        if (isCancelled) return;
        setSessionId(session.sessionId);

        const history = await getSessionMessages(session.sessionId);
        if (isCancelled) return;
        setMessages(history.flatMap(historyTurnToDisplayMessages));
      } catch (err) {
        if (isCancelled) return;
        setError(describeError(err, 'Could not start the chat session'));
      } finally {
        if (!isCancelled) setIsInitializing(false);
      }
    }

    void initializeSession();

    return () => {
      isCancelled = true;
    };
  }, []);

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages]);

  const handleSend = useCallback(async () => {
    const text = draft.trim();
    if (!text || !sessionId || isSending) return;

    const userMessageId = `local-${Date.now()}`;
    setMessages((prev) => [
      ...prev,
      { id: userMessageId, sender: 'user', text },
    ]);
    setDraft('');
    setIsSending(true);
    setError(null);

    try {
      const reply = await sendMessage(sessionId, text);
      setMessages((prev) => [
        ...prev,
        {
          id: reply.turnId,
          sender: 'assistant',
          text: reply.message,
          escalateToHuman: reply.escalateToHuman,
          referencedOrderIds: reply.referencedOrderIds,
        },
      ]);
    } catch (err) {
      setError(describeError(err, 'Your message failed to send'));
    } finally {
      setIsSending(false);
    }
  }, [draft, sessionId, isSending]);

  const inputDisabled = isInitializing || isSending || !sessionId;

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        maxWidth: 480,
        border: '1px solid #d1d5db',
        borderRadius: 12,
        overflow: 'hidden',
      }}
    >
      <div
        style={{
          padding: '10px 16px',
          backgroundColor: '#111827',
          color: '#ffffff',
          fontWeight: 600,
        }}
      >
        Order Support Chat
      </div>

      <div
        style={{
          flex: 1,
          overflowY: 'auto',
          padding: 12,
          backgroundColor: '#f9fafb',
          minHeight: 240,
        }}
      >
        {isInitializing ? (
          <p>Starting chat session…</p>
        ) : (
          <MessageList messages={messages} />
        )}
        <div ref={messagesEndRef} />
      </div>

      {error ? (
        <div style={{ color: '#b91c1c', fontSize: 13, padding: '4px 12px' }}>
          {error}
        </div>
      ) : null}

      <form
        onSubmit={(event) => {
          event.preventDefault();
          void handleSend();
        }}
        style={{
          display: 'flex',
          gap: 8,
          padding: 12,
          borderTop: '1px solid #e5e7eb',
        }}
      >
        <input
          type="text"
          value={draft}
          onChange={(event) => setDraft(event.target.value)}
          placeholder="Ask about an order or policy…"
          disabled={inputDisabled}
          style={{
            flex: 1,
            padding: '8px 10px',
            borderRadius: 8,
            border: '1px solid #d1d5db',
          }}
        />
        <button
          type="submit"
          disabled={inputDisabled || !draft.trim()}
          style={{
            padding: '8px 16px',
            borderRadius: 8,
            border: 'none',
            backgroundColor: '#2563eb',
            color: '#ffffff',
            cursor: 'pointer',
          }}
        >
          Send
        </button>
      </form>
    </div>
  );
}

export default ChatWidget;
