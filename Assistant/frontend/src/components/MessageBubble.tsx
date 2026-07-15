import type { CSSProperties } from 'react';

/** One message shown in the chat transcript (either side of the exchange). */
export interface ChatDisplayMessage {
  id: string;
  sender: 'user' | 'assistant';
  text: string;
  /** Only meaningful for assistant messages; drives the EscalationBanner. */
  escalateToHuman?: boolean;
  /**
   * Order id(s) this assistant reply is about (contracts/chat-api.md's
   * `referencedOrderIds`), surfaced so the customer can tell which order a
   * response refers to. Only meaningful for assistant messages.
   */
  referencedOrderIds?: string[];
}

interface MessageBubbleProps {
  message: ChatDisplayMessage;
}

function containerStyle(sender: ChatDisplayMessage['sender']): CSSProperties {
  return {
    display: 'flex',
    justifyContent: sender === 'user' ? 'flex-end' : 'flex-start',
    margin: '4px 0',
  };
}

function bubbleStyle(sender: ChatDisplayMessage['sender']): CSSProperties {
  const isUser = sender === 'user';
  return {
    maxWidth: '75%',
    padding: '8px 12px',
    borderRadius: 12,
    fontSize: 14,
    lineHeight: 1.4,
    whiteSpace: 'pre-wrap',
    backgroundColor: isUser ? '#2563eb' : '#e5e7eb',
    color: isUser ? '#ffffff' : '#111827',
  };
}

const orderTagStyle: CSSProperties = {
  fontSize: 11,
  fontWeight: 600,
  color: '#6b7280',
  marginBottom: 4,
};

function orderTagText(referencedOrderIds: string[]): string {
  const label = referencedOrderIds.length > 1 ? 'orders' : 'order';
  const ids = referencedOrderIds.map((id) => `#${id}`).join(', ');
  return `Regarding ${label} ${ids}`;
}

function MessageBubble({ message }: MessageBubbleProps) {
  const hasOrderTag =
    message.sender === 'assistant' &&
    (message.referencedOrderIds?.length ?? 0) > 0;

  return (
    <div style={containerStyle(message.sender)}>
      <div style={bubbleStyle(message.sender)}>
        {hasOrderTag ? (
          <div style={orderTagStyle}>
            {orderTagText(message.referencedOrderIds as string[])}
          </div>
        ) : null}
        {message.text}
      </div>
    </div>
  );
}

export default MessageBubble;
