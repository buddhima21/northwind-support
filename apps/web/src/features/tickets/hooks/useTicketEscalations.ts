import { useCallback } from 'react';
import { ticketsApi } from '../../../api/tickets';
import { useAsyncData } from '../../../hooks/useAsyncData';

/** History has its own retry state, so a failed read never hides a saved ticket. */
export function useTicketEscalations(ticketId: number) {
  const load = useCallback(() => ticketsApi.getEscalations(ticketId), [ticketId]);
  return useAsyncData(load, 'Unable to load escalation history.');
}
