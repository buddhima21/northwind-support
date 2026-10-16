import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { formatDateTime } from '../../lib/format';
import { ticketDetail, ticketEscalation } from '../../test/fixtures';
import { TicketDetailPage } from './TicketDetailPage';

const fetchMock = vi.fn<typeof fetch>();
const initial = ticketDetail();
const escalation = ticketEscalation();
const updated = ticketDetail({ priority: 'Critical', assignedAgent: null,
  dueAtUtc: escalation.toDueAtUtc, slaStatus: 'WithinSla', escalation });
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
let failEscalation: boolean;
let historyReads: number;
let failHistoryRefresh: boolean;

beforeEach(() => {
  failEscalation = false;
  failHistoryRefresh = false;
  historyReads = 0;
  fetchMock.mockImplementation(async (input, init) => {
    const url = String(input);
    if (url.endsWith('/escalate') && init?.method === 'POST') {
      return failEscalation ? json({ detail: 'Ticket is already Critical.' }, 409) : json(updated);
    }
    if (url.endsWith('/escalations')) {
      historyReads++;
      if (failHistoryRefresh && historyReads === 2) return json({ detail: 'History temporarily unavailable' }, 503);
      return json(historyReads === 1 ? [] : [escalation]);
    }
    if (url === '/api/tickets/12') return json(initial);
    return json([]);
  });
  vi.stubGlobal('fetch', fetchMock);
});
afterEach(() => { vi.unstubAllGlobals(); vi.resetAllMocks(); });

async function openAndSubmit() {
  render(<MemoryRouter initialEntries={['/tickets/12']}><Routes>
    <Route path="/tickets/:id" element={<TicketDetailPage />} />
  </Routes></MemoryRouter>);
  await screen.findByRole('heading', { name: initial.title });
  await screen.findByText('No escalations yet');
  fireEvent.change(screen.getByLabelText('Reason'), { target: { value: escalation.reason } });
  fireEvent.change(screen.getByLabelText('Escalated by'), { target: { value: escalation.escalatedBy } });
  await userEvent.click(screen.getByRole('button', { name: 'Escalate ticket' }));
}

describe('ticket detail escalation flow', () => {
  it('shows 409 without changing the displayed ticket or reloading history', async () => {
    failEscalation = true;
    await openAndSubmit();
    expect(await screen.findByRole('alert')).toHaveTextContent('Ticket is already Critical.');
    expect(screen.getByText('High')).toBeInTheDocument();
    expect(screen.queryByText('Critical')).not.toBeInTheDocument();
    expect(screen.getByText('Breached')).toBeInTheDocument();
    expect(screen.getByText(formatDateTime(initial.dueAtUtc))).toBeInTheDocument();
    expect(screen.getByLabelText('Reason')).toHaveValue(escalation.reason);
    expect(historyReads).toBe(1);
    expect(screen.getByRole('button', { name: 'Reload ticket' })).toBeEnabled();
  });

  it('updates the saved ticket and refetches complete history without reloading the page', async () => {
    await openAndSubmit();
    expect(await screen.findByRole('status')).toHaveTextContent('Ticket escalated successfully.');
    const history = screen.getByRole('region', { name: 'Escalation history' });
    expect(await within(history).findByText(escalation.reason)).toBeInTheDocument();
    expect(within(history).getByText('High → Critical')).toBeInTheDocument();
    expect(within(history).getByText('Sara Lindqvist → Unassigned')).toBeInTheDocument();
    expect(within(history).getByText(`${formatDateTime(initial.dueAtUtc)} → ${formatDateTime(escalation.toDueAtUtc)}`)).toBeInTheDocument();
    expect(within(history).getByText('alex.lead')).toBeInTheDocument();
    expect(history.querySelector('time')).toHaveAttribute('datetime', escalation.escalatedAtUtc);
    expect(screen.getByText('Within SLA')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Escalate ticket' })).toBeDisabled();
    expect(historyReads).toBe(2);
    expect(fetchMock.mock.calls.filter(([url]) => url === '/api/tickets/12')).toHaveLength(1);
  });

  it('keeps the saved ticket when history refresh fails and allows retrying history alone', async () => {
    failHistoryRefresh = true;
    await openAndSubmit();
    expect(await screen.findByRole('alert')).toHaveTextContent('History temporarily unavailable');
    expect(screen.getByText('Critical')).toBeInTheDocument();
    const history = screen.getByRole('region', { name: 'Escalation history' });
    await userEvent.click(within(history).getByRole('button', { name: /try again/i }));
    await waitFor(() => expect(within(history).getByText(escalation.reason)).toBeInTheDocument());
    expect(historyReads).toBe(3);
    expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(1);
  });
});
