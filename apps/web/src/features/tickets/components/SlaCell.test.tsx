import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { SlaStatus } from '../../../types/api';
import { SlaCell } from './SlaCell';

describe('shared SLA indicator', () => {
  it.each<[SlaStatus, string, string]>([
    ['WithinSla', 'Within SLA', 'info'], ['AtRisk', 'At risk', 'warning'],
    ['Breached', 'Breached', 'danger'], ['Met', 'Met', 'success'],
    ['NotApplicable', 'No SLA', 'neutral'],
  ])('renders %s with its own readable label and tone', (status, label, tone) => {
    render(<SlaCell slaStatus={status} dueAtUtc={null} />);
    expect(screen.getByText(label)).toHaveClass(`badge--${tone}`);
  });

  it('shows a machine-readable due date alongside the state', () => {
    const due = '2026-10-16T12:00:00Z';
    const { container } = render(<SlaCell slaStatus="AtRisk" dueAtUtc={due} />);
    expect(container.querySelector('time')).toHaveAttribute('datetime', due);
    expect(container.querySelector('time')).not.toBeEmptyDOMElement();
  });
});
