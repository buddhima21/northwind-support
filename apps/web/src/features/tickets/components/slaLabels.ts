import type { BadgeTone } from '../../../components/Badge';
import type { SlaStatus } from '../../../types/api';

export const slaLabels: Record<SlaStatus, { text: string; tone: BadgeTone }> = {
  Met: { text: 'Met', tone: 'success' },
  Breached: { text: 'Breached', tone: 'danger' },
  WithinSla: { text: 'Within SLA', tone: 'info' },
  AtRisk: { text: 'At risk', tone: 'warning' },
  NotApplicable: { text: 'No SLA', tone: 'neutral' },
};
