import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { formatDateTime } from '../../lib/format';
import { ticketDetail } from '../../test/fixtures';
import { CreateTicketPage } from './CreateTicketPage';

afterEach(() => { vi.unstubAllGlobals(); });

describe('create ticket triage result', () => {
  it.each([true, false])('shows the saved priority, deadline and assignment explanation (assigned=%s)', async (assigned) => {
    const saved = ticketDetail({
      priority: 'Critical',
      assignedAgent: assigned ? { id: 3, fullName: 'Priya Nair' } : null,
      triage: { appliedPriority: 'Critical', priorityReason: "Category 'Security' is always treated as Critical",
        slaWindowHours: 2, slaReason: 'Critical base window halved for a Premium customer',
        assignmentReason: assigned ? 'Fewest open tickets among Security specialists' : 'No available agent specialised in Security' },
    });
    const fetchMock = vi.fn<typeof fetch>(async (input, init) => {
      if (init?.method === 'POST') return new Response(JSON.stringify(saved), { status: 201 });
      if (String(input) === '/api/customers') return new Response(JSON.stringify([{ id: 2, name: 'Contoso', tier: 'Premium' }]));
      if (String(input) === '/api/categories') return new Response(JSON.stringify([{ id: 3, name: 'Security', isActive: true }]));
      return new Response('[]');
    });
    vi.stubGlobal('fetch', fetchMock);
    render(<MemoryRouter><CreateTicketPage /></MemoryRouter>);
    await screen.findByRole('button', { name: 'Raise ticket' });
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Security issue with the website' } });
    fireEvent.change(screen.getByLabelText('Description'), { target: { value: 'The customer reports a possible data exposure.' } });
    fireEvent.change(screen.getByLabelText('Customer'), { target: { value: '2' } });
    fireEvent.change(screen.getByLabelText('Category'), { target: { value: '3' } });
    fireEvent.change(screen.getByLabelText('Requested priority'), { target: { value: 'Low' } });
    await userEvent.click(screen.getByRole('button', { name: 'Raise ticket' }));
    const triage = await screen.findByRole('region', { name: 'Triage result' });
    expect(within(triage).getByText('Critical')).toBeInTheDocument();
    expect(within(triage).getByText(formatDateTime(saved.dueAtUtc))).toBeInTheDocument();
    expect(within(triage).getByText('2 hour SLA window')).toBeInTheDocument();
    expect(within(triage).getByText(saved.triage!.priorityReason)).toBeInTheDocument();
    expect(within(triage).getByText(saved.triage!.slaReason)).toBeInTheDocument();
    expect(within(triage).getByText(assigned ? 'Priya Nair' : 'Unassigned')).toBeInTheDocument();
    expect(within(triage).getByText(saved.triage!.assignmentReason)).toBeInTheDocument();
    const create = fetchMock.mock.calls.find(([, init]) => init?.method === 'POST');
    expect(JSON.parse(String(create?.[1]?.body)).requestedPriority).toBe('Low');
    await userEvent.click(screen.getByRole('button', { name: 'Raise another' }));
    expect(screen.queryByRole('region', { name: 'Triage result' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Raise ticket' })).toBeInTheDocument();
  });
});
