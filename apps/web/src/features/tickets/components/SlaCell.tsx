import { Badge } from '../../../components/Badge';
import { formatDateTime } from '../../../lib/format';
import type { SlaStatus } from '../../../types/api';

import { slaLabels } from './slaLabels';

interface SlaCellProps {
  dueAtUtc: string | null;
  slaStatus: SlaStatus;
}

/** Shared list/detail indicator. Text identifies every state without relying on colour. */
export function SlaCell({ dueAtUtc, slaStatus }: SlaCellProps) {
  const label = slaLabels[slaStatus];

  return (
    <div className="sla-cell">
      <Badge tone={label.tone}>{label.text}</Badge>
      {dueAtUtc && (
        <time className="sla-cell__due" dateTime={dueAtUtc}>{formatDateTime(dueAtUtc)}</time>
      )}
    </div>
  );
}
