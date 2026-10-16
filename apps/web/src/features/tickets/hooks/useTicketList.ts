import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import type { PagedResult, TicketFilters, TicketListItem } from '../../../types/api';

const DEBOUNCE_MS = 300;

export const emptyFilters: TicketFilters = {
  search: '',
  status: undefined,
  priority: undefined,
  slaStatus: undefined,
  categoryId: undefined,
  customerId: undefined,
  assignedAgentId: undefined,
  unassignedOnly: false,
  page: 1,
  pageSize: 20,
  sortBy: 'createdAtUtc',
  sortDirection: 'desc',
};

interface State {
  isLoading: boolean;
  completedFilters?: TicketFilters;
  data?: PagedResult<TicketListItem>;
  error?: string;
}

/** Owns the ticket list's filters and the data they produce. */
export function useTicketList() {
  const [filters, setFilters] = useState<TicketFilters>(emptyFilters);
  // Keep typing responsive without issuing a request for every keystroke.
  const [search, setSearch] = useState('');
  const [state, setState] = useState<State>({ isLoading: true });
  const requestId = useRef(0);

  useEffect(() => {
    const handle = window.setTimeout(() => {
      setFilters((current) => current.search === search
        ? current
        : { ...current, search, page: 1 });
    }, DEBOUNCE_MS);
    return () => window.clearTimeout(handle);
  }, [search]);

  const load = useCallback(() => {
    const id = ++requestId.current;
    return ticketsApi.getTickets(filters).then(
      (data) => {
        if (id === requestId.current) {
          setState({ isLoading: false, completedFilters: filters, data, error: undefined });
        }
      },
      (error: unknown) => {
        if (id === requestId.current) {
          const message = error instanceof ApiError ? error.message : 'Unable to load tickets.';
          setState((current) => ({ ...current, isLoading: false, completedFilters: filters, error: message }));
        }
      },
    );
  }, [filters]);

  useEffect(() => {
    void load();
    // Invalidate in-flight responses on filter changes and unmount.
    return () => { requestId.current += 1; };
  }, [load]);

  const updateFilters = ({ search: nextSearch, ...patch }: Partial<TicketFilters>) => {
    if (nextSearch !== undefined) {
      setSearch(nextSearch);
    }
    // Observe the entire filter object, including filters added in future tasks.
    if (Object.keys(patch).length > 0) {
      setFilters((current) => ({ ...current, ...patch, page: patch.page ?? 1 }));
    }
  };

  const resetFilters = () => {
    setSearch('');
    setFilters(emptyFilters);
  };

  const reload = () => {
    setState((current) => ({ ...current, isLoading: true, error: undefined }));
    return load();
  };

  // A changed query is loading until its own response arrives; no effect needs to
  // synchronously update loading state just because filters changed.
  const isCurrent = state.completedFilters === filters;
  return {
    filters: { ...filters, search },
    updateFilters,
    resetFilters,
    reload,
    data: state.data,
    error: isCurrent ? state.error : undefined,
    isLoading: !isCurrent || state.isLoading,
  };
}
