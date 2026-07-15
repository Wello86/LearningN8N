import ChatWidget from '../components/ChatWidget';

/** Page that hosts the order-support chat widget. */
function ChatPage() {
  return (
    <div
      style={{
        display: 'flex',
        justifyContent: 'center',
        padding: 24,
        height: '100vh',
        boxSizing: 'border-box',
      }}
    >
      <ChatWidget />
    </div>
  );
}

export default ChatPage;
