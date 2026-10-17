import { afterEach, describe, expect, it, vi } from 'vitest';
import { loginSession } from '../../test/authFixtures';
import { clearSession, getAuthState, restoreStoredSession, sessionStorageKey, setSession } from './session';
import { returnTo } from './returnTo';

afterEach(() => { vi.restoreAllMocks(); clearSession(); });

describe('stored sessions', () => {
  it.each(['broken-json', '{}', 'null', '{"accessToken":4}'])('discards malformed storage: %s', (raw) => {
    sessionStorage.setItem(sessionStorageKey, raw);
    restoreStoredSession();
    expect(getAuthState().status).toBe('anonymous');
    expect(sessionStorage.getItem(sessionStorageKey)).toBeNull();
  });

  it('requires server verification for persisted credentials and discards expired credentials', () => {
    const session = loginSession();
    sessionStorage.setItem(sessionStorageKey, JSON.stringify(session));
    restoreStoredSession();
    expect(getAuthState()).toEqual({ status: 'restoring', session });
    sessionStorage.setItem(sessionStorageKey, JSON.stringify(loginSession({ expiresAtUtc: '2000-01-01T00:00:00Z' })));
    restoreStoredSession();
    expect(getAuthState().status).toBe('anonymous');
    expect(sessionStorage.getItem(sessionStorageKey)).toBeNull();
  });

  it('works in memory when the browser blocks persistence', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new DOMException('Blocked'); });
    const session = loginSession();
    expect(() => setSession(session)).not.toThrow();
    expect(getAuthState().session).toEqual(session);
  });
});

describe('login return destinations', () => {
  it.each(['https://evil.example', '//evil.example', '/\\evil.example', '/login', '/login?again=1',
    '/LOGIN/', '/login/', '/%6cogin', '/%broken'])(
    'rejects an external destination or login loop: %s', (from) => expect(returnTo({ from })).toBe('/tickets'),
  );

  it('keeps local paths including their query and fragment', () => {
    expect(returnTo({ from: '/tickets/12?view=history#escalations' })).toBe('/tickets/12?view=history#escalations');
    expect(returnTo(undefined)).toBe('/tickets');
  });
});
