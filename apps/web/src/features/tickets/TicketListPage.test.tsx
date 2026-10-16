import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { page, ticketListItem } from '../../test/fixtures';
import { TicketListPage } from './TicketListPage';

afterEach(() => { vi.unstubAllGlobals(); });

describe('SLA filter in the ticket list', () => {
  it('sends the selected SLA state immediately and removes it when cleared', async () => {
    const fetchMock = vi.fn<typeof fetch>(async (input) => {
      const url = new URL(String(input), 'http://localhost');
      return new Response(JSON.stringify(url.pathname === '/api/tickets'
        ? page([ticketListItem({ slaStatus: url.searchParams.get('slaStatus') === 'AtRisk' ? 'AtRisk' : 'WithinSla' })]) : []));
    });
    vi.stubGlobal('fetch', fetchMock);
    render(<MemoryRouter><TicketListPage /></MemoryRouter>);
    await screen.findByRole('link', { name: 'TCK-0012' });
    const queries = () => fetchMock.mock.calls.filter(([url]) => String(url).startsWith('/api/tickets?'));
    fireEvent.change(screen.getByLabelText('SLA status'), { target: { value: 'AtRisk' } });
    await waitFor(() => expect(queries()).toHaveLength(2));
    expect(new URL(String(queries().at(-1)?.[0]), 'http://localhost').searchParams.get('slaStatus')).toBe('AtRisk');
    expect(await screen.findByText('At risk', { selector: '.badge' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Clear filters' }));
    await waitFor(() => expect(queries()).toHaveLength(3));
    expect(new URL(String(queries().at(-1)?.[0]), 'http://localhost').searchParams.has('slaStatus')).toBe(false);
    expect(screen.getByLabelText('SLA status')).toHaveValue('');
  });
});
