import { useRef, useState } from 'react';
import { ApiError, toErrorMessage } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import { Field } from '../../../components/Field';
import type { TicketDetail } from '../../../types/api';

interface EscalationPanelProps {
  ticket: TicketDetail;
  onUpdated: (ticket: TicketDetail) => void;
  onReload: () => void;
}

/** The server owns escalation decisions; this form validates only the request inputs. */
export function EscalationPanel({ ticket, onUpdated, onReload }: EscalationPanelProps) {
  const [reason, setReason] = useState('');
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [error, setError] = useState<string>();
  const [conflict, setConflict] = useState(false);
  const [isSaving, setIsSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const submitting = useRef(false);
  const blocked = ticket.status === 'Resolved' || ticket.status === 'Closed'
    ? `${ticket.status} tickets cannot be escalated.`
    : ticket.priority === 'Critical'
      ? 'Critical tickets cannot be escalated further.'
      : undefined;

  const submit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (blocked || submitting.current) {
      return;
    }
    const payload = { reason: reason.trim() };
    const validation: Record<string, string> = {};
    if (payload.reason.length < 5 || payload.reason.length > 500) {
      validation.reason = 'Enter a reason of 5 to 500 characters.';
    }
    setErrors(validation);
    setError(undefined);
    setConflict(false);
    setSaved(false);
    if (Object.keys(validation).length > 0) {
      return;
    }

    submitting.current = true;
    setIsSaving(true);
    try {
      const updated = await ticketsApi.escalateTicket(ticket.id, payload);
      onUpdated(updated);
      setReason('');
      setSaved(true);
    } catch (caught) {
      if (caught instanceof ApiError) {
        setConflict(caught.status === 409);
        setErrors(Object.fromEntries(
          Object.entries(caught.fieldErrors).map(([field, messages]) => [
            field.charAt(0).toLowerCase() + field.slice(1), messages[0],
          ]),
        ));
      }
      setError(toErrorMessage(caught, 'Could not escalate the ticket.'));
    } finally {
      submitting.current = false;
      setIsSaving(false);
    }
  };

  return (
    <section className="card" aria-label="Ticket escalation">
      <h2>Escalate ticket</h2>
      <p>Raise priority one level and recalculate the due date and assignment.</p>
      {blocked && (
        <p id="escalation-blocked" className="field__hint">{blocked}</p>
      )}
      <form className="form" onSubmit={(event) => void submit(event)} noValidate aria-busy={isSaving}>
        <fieldset className="escalation-fields" disabled={isSaving || Boolean(blocked)}>
          <legend className="visually-hidden">Escalation details</legend>
          <Field
            id="escalation-reason"
            label="Reason"
            error={errors.reason}
            hint="5 to 500 characters, excluding surrounding spaces."
          >
            {(props) => (
              <textarea
                {...props}
                rows={3}
                value={reason}
                onChange={(event) => setReason(event.target.value)}
              />
            )}
          </Field>
          <button
            type="submit"
            className="button button--primary"
            aria-describedby={blocked ? 'escalation-blocked' : undefined}
          >
            {isSaving ? 'Escalating...' : 'Escalate ticket'}
          </button>
        </fieldset>
      </form>
      {error && (
        <div className="error" role="alert">
          <p>{error}</p>
          {conflict && (
            <button type="button" className="button" onClick={onReload}>Reload ticket</button>
          )}
        </div>
      )}
      {saved && <p role="status">Ticket escalated successfully.</p>}
    </section>
  );
}
