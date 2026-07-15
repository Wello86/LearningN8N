import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import ChatWidget from '../src/components/ChatWidget';
import {
  createSession,
  getSessionMessages,
  sendMessage,
} from '../src/services/chatApiClient';

vi.mock('../src/services/chatApiClient');

const mockCreateSession = vi.mocked(createSession);
const mockGetSessionMessages = vi.mocked(getSessionMessages);
const mockSendMessage = vi.mocked(sendMessage);

const SESSION_ID = 'session-1';

beforeEach(() => {
  mockCreateSession.mockResolvedValue({
    sessionId: SESSION_ID,
    createdAt: '2026-07-13T00:00:00Z',
  });
  mockGetSessionMessages.mockResolvedValue([]);
});

afterEach(() => {
  vi.resetAllMocks();
});

describe('ChatWidget', () => {
  it('starts a session and renders no history when there is none', async () => {
    render(<ChatWidget />);

    await waitFor(() => expect(mockCreateSession).toHaveBeenCalledTimes(1));
    expect(mockGetSessionMessages).toHaveBeenCalledWith(SESSION_ID);
  });

  it('renders a US3 combined order+policy reply as one bubble, tagged with its order id, without an escalation banner', async () => {
    mockSendMessage.mockResolvedValue({
      turnId: 'turn-1',
      message:
        "Your order is delayed, and since that's more than 7 days beyond the estimate, " +
        'you are eligible for a shipping fee refund.',
      escalateToHuman: false,
      referencedOrderIds: ['12345'],
    });

    const user = userEvent.setup();
    render(<ChatWidget />);
    await waitFor(() => expect(mockCreateSession).toHaveBeenCalledTimes(1));

    await user.type(
      screen.getByPlaceholderText(/ask about an order or policy/i),
      'My order 12345 seems delayed - am I eligible for a refund under the delay policy?',
    );
    await user.click(screen.getByRole('button', { name: /send/i }));

    expect(
      await screen.findByText(/eligible for a shipping fee refund/i),
    ).toBeInTheDocument();
    expect(screen.getByText('Regarding order #12345')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('tags a combined reply that references multiple orders in the plural, and shows the escalation banner when flagged', async () => {
    mockSendMessage.mockResolvedValue({
      turnId: 'turn-2',
      message: "Here's your order 12345 update; I could not find a policy for that other question.",
      escalateToHuman: true,
      referencedOrderIds: ['12345', '12347'],
    });

    const user = userEvent.setup();
    render(<ChatWidget />);
    await waitFor(() => expect(mockCreateSession).toHaveBeenCalledTimes(1));

    await user.type(
      screen.getByPlaceholderText(/ask about an order or policy/i),
      'Where are my orders 12345 and 12347, and do you offer gift wrapping?',
    );
    await user.click(screen.getByRole('button', { name: /send/i }));

    await screen.findByText(/could not find a policy/i);
    expect(screen.getByText('Regarding orders #12345, #12347')).toBeInTheDocument();
    expect(screen.getByRole('alert')).toBeInTheDocument();
  });

  it('restores prior turns from session history, including their order tags and escalation state', async () => {
    mockGetSessionMessages.mockResolvedValue([
      {
        turnId: 'turn-0',
        userMessage: 'Where is my order 12346?',
        message: 'Your order 12346 was delivered on time.',
        escalateToHuman: false,
        referencedOrderIds: ['12346'],
      },
    ]);

    render(<ChatWidget />);

    expect(await screen.findByText('Where is my order 12346?')).toBeInTheDocument();
    expect(screen.getByText('Your order 12346 was delivered on time.')).toBeInTheDocument();
    expect(screen.getByText('Regarding order #12346')).toBeInTheDocument();
  });
});
