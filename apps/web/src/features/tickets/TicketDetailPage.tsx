import { Link, useParams } from 'react-router-dom';
import { ErrorMessage } from '../../components/ErrorMessage';
import { Spinner } from '../../components/Spinner';
import { useReferenceData } from '../../hooks/useReferenceData';
import { AssignAgentControl } from './components/AssignAgentControl';
import { StatusActions } from './components/StatusActions';
import { TicketSummary } from './components/TicketSummary';
import { useTicket } from './hooks/useTicket';
import type { TicketDetail } from '../../types/api';
import { EscalationPanel } from './components/EscalationPanel';
import { EscalationHistory } from './components/EscalationHistory';
import { TriageSummary } from './components/TriageSummary';
import { useTicketEscalations } from './hooks/useTicketEscalations';

export function TicketDetailPage() {
  const { id } = useParams<{ id: string }>();
  const ticketId = Number(id);

  const { data: ticket, isLoading, error, reload, setData } = useTicket(ticketId);

  if (Number.isNaN(ticketId)) {
    return <ErrorMessage message="That is not a valid ticket reference." />;
  }

  if (isLoading && !ticket) {
    return <Spinner label="Loading ticket" />;
  }

  if (error && !ticket) {
    return <ErrorMessage message={error} onRetry={() => void reload()} />;
  }

  if (!ticket) {
    return null;
  }

  return <TicketDetailContent key={ticket.id} ticket={ticket} error={error} reload={reload} onUpdated={setData} />;
}

function TicketDetailContent({ ticket, error, reload, onUpdated }: {
  ticket: TicketDetail;
  error?: string;
  reload: () => Promise<void>;
  onUpdated: (ticket: TicketDetail) => void;
}) {
  const reference = useReferenceData();
  const history = useTicketEscalations(ticket.id);
  const escalated = (updated: TicketDetail) => {
    onUpdated(updated);
    void history.reload();
    void reference.reload();
  };

  return (
    <section>
      <header className="page-header">
        <div>
          <p className="breadcrumb">
            <Link to="/tickets">Tickets</Link> / {ticket.reference}
          </p>
          <h1>{ticket.title}</h1>
        </div>
      </header>

      {error && <ErrorMessage message={error} onRetry={() => void reload()} />}

      <div className="detail-layout">
        <div className="detail-layout__main">
          <div className="card">
            <h2>Description</h2>
            <p className="description">{ticket.description}</p>
          </div>

          <StatusActions ticket={ticket} onUpdated={onUpdated} />
          <EscalationPanel ticket={ticket} onUpdated={escalated} onReload={() => void reload()} />
          {ticket.triage && <TriageSummary ticket={ticket} />}
          <EscalationHistory history={history} />
        </div>

        <aside className="detail-layout__side">
          <TicketSummary ticket={ticket} />

          <AssignAgentControl
            ticket={ticket}
            agents={reference.data?.agents ?? []}
            onUpdated={onUpdated}
          />
        </aside>
      </div>
    </section>
  );
}
