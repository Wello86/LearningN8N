import MessageBubble from './MessageBubble';
import type { ChatDisplayMessage } from './MessageBubble';
import EscalationBanner from './EscalationBanner';

interface MessageListProps {
  messages: ChatDisplayMessage[];
}

/** Renders an ordered chat transcript, one bubble per message. */
function MessageList({ messages }: MessageListProps) {
  return (
    <div>
      {messages.map((message) => (
        <div key={message.id}>
          <MessageBubble message={message} />
          {message.sender === 'assistant' && message.escalateToHuman ? (
            <EscalationBanner />
          ) : null}
        </div>
      ))}
    </div>
  );
}

export default MessageList;
