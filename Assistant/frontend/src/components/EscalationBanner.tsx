import type { CSSProperties } from 'react';

const bannerStyle: CSSProperties = {
  backgroundColor: '#fef3c7',
  border: '1px solid #f59e0b',
  color: '#92400e',
  borderRadius: 8,
  padding: '8px 12px',
  fontSize: 13,
  margin: '4px 0 8px',
};

/**
 * Shown alongside an assistant message whose `escalateToHuman` flag is true
 * (constitution Principle VII / contracts/chat-api.md). This flag is the
 * only signal used to decide whether to show the banner — never inferred
 * from the message text.
 */
function EscalationBanner() {
  return (
    <div style={bannerStyle} role="alert">
      This may need a human agent — we&apos;re connecting you with support
      for this request.
    </div>
  );
}

export default EscalationBanner;
