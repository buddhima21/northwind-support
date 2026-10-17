import { act, fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ticketDetail } from '../../../test/fixtures';
import type { TicketDetail } from '../../../types/api';
import { EscalationPanel } from './EscalationPanel';

afterEach(() => { vi.unstubAllGlobals(); });

describe('EscalationPanel', () => {
  it.each(['', '    ', '  abc  ', 'x'.repeat(501)])('blocks an invalid reason before calling the API (%s)', async (reason) => {
    const fetchMock = vi.fn<typeof fetch>();
    vi.stubGlobal('fetch', fetchMock);
    render(<EscalationPanel ticket={ticketDetail()} onUpdated={vi.fn()} onReload={vi.fn()} />);
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: reason } });
    await userEvent.click(screen.getByRole('button', { name: 'Escalate ticket' }));
    expect(screen.getByText('Enter a reason of 5 to 500 characters.')).toBeInTheDocument();
    expect(screen.getByLabelText('Reason')).toHaveAttribute('aria-invalid', 'true');
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('does not offer an editable escalation actor', () => {
    render(<EscalationPanel ticket={ticketDetail()} onUpdated={vi.fn()} onReload={vi.fn()} />);
    expect(screen.queryByLabelText('Escalated by')).not.toBeInTheDocument();
  });

  it.each<Partial<TicketDetail>>([{ priority: 'Critical' }, { status: 'Resolved' }, { status: 'Closed' }])('disables escalation when the ticket is ineligible: %j', (overrides) => {
    render(<EscalationPanel ticket={ticketDetail(overrides)} onUpdated={vi.fn()} onReload={vi.fn()} />);
    expect(screen.getByRole('button', { name: 'Escalate ticket' })).toBeDisabled();
    expect(screen.getByLabelText('Reason')).toBeDisabled();
  });

  it.each([5, 500])('trims input, accepts a %i-character reason and prevents duplicate pending submissions', async (length) => {
    let finish!: (response: Response) => void;
    const fetchMock = vi.fn<typeof fetch>(() => new Promise((resolve) => { finish = resolve; }));
    vi.stubGlobal('fetch', fetchMock);
    const onUpdated = vi.fn();
    render(<EscalationPanel ticket={ticketDetail()} onUpdated={onUpdated} onReload={vi.fn()} />);
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: `  ${'x'.repeat(length)}  ` } });
    await userEvent.click(screen.getByRole('button', { name: 'Escalate ticket' }));
    expect(screen.getByRole('button', { name: 'Escalating...' })).toBeDisabled();
    expect(screen.getByLabelText('Reason')).toBeDisabled();
    fireEvent.submit(screen.getByRole('button', { name: 'Escalating...' }).closest('form')!);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(JSON.parse(String(fetchMock.mock.calls[0][1]?.body))).toEqual({ reason: 'x'.repeat(length) });
    const updated = ticketDetail({ priority: 'Critical' });
    await act(async () => { finish(new Response(JSON.stringify(updated))); });
    expect(onUpdated).toHaveBeenCalledWith(updated);
    expect(screen.getByRole('status')).toHaveTextContent('Ticket escalated successfully.');
  });

  it('shows server field validation and retains the draft for correction', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockResolvedValue(new Response(JSON.stringify({
      detail: 'Validation failed', errors: { Reason: ['Please give more detail.'] },
    }), { status: 400 })));
    const onUpdated = vi.fn();
    render(<EscalationPanel ticket={ticketDetail()} onUpdated={onUpdated} onReload={vi.fn()} />);
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: 'Valid reason' } });
    await userEvent.click(screen.getByRole('button', { name: 'Escalate ticket' }));
    expect(await screen.findByText('Please give more detail.')).toBeInTheDocument();
    expect(screen.getByLabelText('Reason')).toHaveValue('Valid reason');
    expect(onUpdated).not.toHaveBeenCalled();
  });
});
