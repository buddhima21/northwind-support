import { act, renderHook } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { useAsyncData } from './useAsyncData';

describe('useAsyncData request ordering', () => {
  it('keeps refreshed history when an older initial read completes last', async () => {
    let finishOld!: (value: string[]) => void;
    const load = vi.fn<() => Promise<string[]>>()
      .mockImplementationOnce(() => new Promise((resolve) => { finishOld = resolve; }))
      .mockResolvedValueOnce(['new escalation']);
    const { result } = renderHook(() => useAsyncData(load, 'History failed'));
    await act(async () => { await result.current.reload(); });
    await act(async () => { finishOld([]); });
    expect(result.current.data).toEqual(['new escalation']);
  });

  it('keeps a saved ticket when an older read fails afterwards', async () => {
    let failOld!: (error: Error) => void;
    const load = vi.fn<() => Promise<string>>()
      .mockImplementationOnce(() => new Promise((_, reject) => { failOld = reject; }));
    const { result } = renderHook(() => useAsyncData(load, 'Ticket failed'));
    act(() => { result.current.setData('saved ticket'); });
    await act(async () => { failOld(new Error('old failure')); });
    expect(result.current.data).toBe('saved ticket');
    expect(result.current.error).toBeUndefined();
  });
});
