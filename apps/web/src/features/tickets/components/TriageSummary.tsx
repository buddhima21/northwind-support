import { formatDateTime } from '../../../lib/format';
import type { TicketDetail } from '../../../types/api';
import { PriorityBadge } from './TicketBadges';

export function TriageSummary({ ticket }: { ticket: TicketDetail }) {
  return (
    <section className="card" aria-label="Triage result">
      <h2>Triage result</h2>
      <dl className="summary">
        <div>
          <dt>Applied priority</dt>
          <dd>
            <PriorityBadge priority={ticket.priority} />
            {ticket.triage && <span className="summary__note">{ticket.triage.priorityReason}</span>}
          </dd>
        </div>
        <div>
          <dt>Due date</dt>
          <dd>
            {formatDateTime(ticket.dueAtUtc)}
            {ticket.triage && (
              <>
                <span className="summary__note">{ticket.triage.slaWindowHours} hour SLA window</span>
                <span className="summary__note">{ticket.triage.slaReason}</span>
              </>
            )}
          </dd>
        </div>
        <div>
          <dt>Assigned agent</dt>
          <dd>
            {ticket.assignedAgent?.fullName ?? 'Unassigned'}
            {ticket.triage && <span className="summary__note">{ticket.triage.assignmentReason}</span>}
          </dd>
        </div>
      </dl>
    </section>
  );
}
