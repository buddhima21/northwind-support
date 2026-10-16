import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { page, ticketListItem } from '../../../test/fixtures';
import type { TicketFilters } from '../../../types/api';
import { useTicketList } from './useTicketList';

const fetchMock = vi.fn<typeof fetch>();

function lastQuery() {
  const url = String(fetchMock.mock.calls.at(-1)?.[0]);
  return new URL(url, 'http://localhost').searchParams;
}

async function settle() {
  await act(async () => {});
}

describe('useTicketList', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    fetchMock.mockImplementation(async () => new Response(JSON.stringify(page([ticketListItem()]))));
    vi.stubGlobal('fetch', fetchMock);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
    vi.resetAllMocks();
  });

  const changes: [string, Partial<TicketFilters>, string, string][] = [
    ['status', { status: 'InProgress' }, 'status', 'InProgress'],
    ['priority', { priority: 'High' }, 'priority', 'High'],
    ['SLA status', { slaStatus: 'Breached' }, 'slaStatus', 'Breached'],
    ['category', { categoryId: 2 }, 'categoryId', '2'],
    ['customer', { customerId: 3 }, 'customerId', '3'],
    ['agent', { assignedAgentId: 4 }, 'assignedAgentId', '4'],
    ['unassigned only', { unassignedOnly: true }, 'unassignedOnly', 'true'],
    ['sort field', { sortBy: 'priority' }, 'sortBy', 'priority'],
    ['sort direction', { sortDirection: 'asc' }, 'sortDirection', 'asc'],
    ['page', { page: 2 }, 'page', '2'],
    ['page size', { pageSize: 10 }, 'pageSize', '10'],
  ];

  it.each(changes)('requests immediately when only %s changes', async (_, patch, key, value) => {
    const { result } = renderHook(() => useTicketList());
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });
    fetchMock.mockClear();

    await act(async () => { result.current.updateFilters(patch); });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(lastQuery().get(key)).toBe(value);
  });

  it('debounces search typing and sends only the final text', async () => {
    const { result } = renderHook(() => useTicketList());
    await settle();
    expect(fetchMock).toHaveBeenCalledTimes(1);

    act(() => { result.current.updateFilters({ search: 'inv' }); });
    await act(async () => { await vi.advanceTimersByTimeAsync(200); });
    act(() => { result.current.updateFilters({ search: 'invoice' }); });
    await act(async () => { await vi.advanceTimersByTimeAsync(299); });
    expect(fetchMock).toHaveBeenCalledTimes(1);
    await act(async () => { await vi.advanceTimersByTimeAsync(1); });

    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(lastQuery().get('search')).toBe('invoice');
    expect(result.current.isLoading).toBe(false);
  });

  it('requests status changes even after search has been cleared', async () => {
    const { result } = renderHook(() => useTicketList());
    await settle();
    act(() => { result.current.updateFilters({ search: 'invoice' }); });
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });
    act(() => { result.current.updateFilters({ search: '' }); });
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });
    fetchMock.mockClear();

    await act(async () => { result.current.updateFilters({ status: 'InProgress' }); });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(lastQuery().get('status')).toBe('InProgress');
    expect(lastQuery().has('search')).toBe(false);
  });

  it('does not delay a dropdown change while search is pending', async () => {
    const { result } = renderHook(() => useTicketList());
    await settle();
    act(() => { result.current.updateFilters({ search: 'invoice' }); });
    await act(async () => { result.current.updateFilters({ status: 'InProgress' }); });
    expect(lastQuery().get('status')).toBe('InProgress');

    await act(async () => { await vi.advanceTimersByTimeAsync(300); });
    expect(lastQuery().get('search')).toBe('invoice');
    expect(lastQuery().get('status')).toBe('InProgress');
  });

  it('resets pagination when a filter changes and clears all filters', async () => {
    const { result } = renderHook(() => useTicketList());
    await settle();
    await act(async () => { result.current.updateFilters({ page: 3 }); });
    await act(async () => { result.current.updateFilters({ priority: 'High' }); });
    expect(lastQuery().get('page')).toBe('1');
    await act(async () => { result.current.resetFilters(); });
    expect(lastQuery().has('priority')).toBe(false);
    expect(result.current.filters.search).toBe('');
  });

  it('keeps the latest results when an older request finishes last', async () => {
    let finishOld!: (response: Response) => void;
    fetchMock.mockImplementationOnce(() => new Promise<Response>((resolve) => { finishOld = resolve; }));
    const { result } = renderHook(() => useTicketList());
    await settle();
    const latest = ticketListItem({ id: 42, status: 'InProgress' });
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(page([latest]))));
    await act(async () => { result.current.updateFilters({ status: 'InProgress' }); });
    await act(async () => { finishOld(new Response(JSON.stringify(page([ticketListItem()])))); });
    expect(result.current.data?.items[0].id).toBe(42);
  });

  it('shows request errors and allows retrying the current filters', async () => {
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify({ detail: 'Temporarily unavailable' }), { status: 503 }));
    const { result } = renderHook(() => useTicketList());
    await settle();
    expect(result.current.error).toBe('Temporarily unavailable');
    await act(async () => { await result.current.reload(); });
    expect(result.current.error).toBeUndefined();
    expect(result.current.data?.items).toHaveLength(1);
  });
});
