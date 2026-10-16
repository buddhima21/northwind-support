import { EmptyState } from '../../../components/EmptyState';
import { ErrorMessage } from '../../../components/ErrorMessage';
import { Spinner } from '../../../components/Spinner';
import type { AsyncData } from '../../../hooks/useAsyncData';
import { formatDateTime } from '../../../lib/format';
import type { TicketEscalation } from '../../../types/api';

export function EscalationHistory({ history }: { history: AsyncData<TicketEscalation[]> }) {
  return (
    <section className="card" aria-label="Escalation history" aria-busy={history.isLoading}>
      <h2>Escalation history</h2>
      {history.isLoading && <Spinner label="Loading escalation history" />}
      {history.error && <ErrorMessage message={history.error} onRetry={() => void history.reload()} />}
      {!history.isLoading && !history.error && history.data?.length === 0 && (
        <EmptyState title="No escalations yet" description="Escalations will appear here, newest first." />
      )}
      {history.data && history.data.length > 0 && (
        <ol className="escalation-history">
          {history.data.map((entry) => (
            <li key={entry.id}>
              <p>
                <strong>{entry.escalatedBy}</strong> escalated this ticket at{' '}
                <time dateTime={entry.escalatedAtUtc}>{formatDateTime(entry.escalatedAtUtc)}</time>.
              </p>
              <dl className="summary">
                <div>
                  <dt>Priority change</dt>
                  <dd>{entry.fromPriority} → {entry.toPriority}</dd>
                </div>
                <div>
                  <dt>Agent change</dt>
                  <dd>{entry.fromAgent?.fullName ?? 'Unassigned'} → {entry.toAgent?.fullName ?? 'Unassigned'}</dd>
                </div>
                <div>
                  <dt>Due-date change</dt>
                  <dd>{formatDateTime(entry.fromDueAtUtc)} → {formatDateTime(entry.toDueAtUtc)}</dd>
                </div>
              </dl>
              <p className="description">{entry.reason}</p>
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}
