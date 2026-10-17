import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { authApi } from './auth';
import { request } from './client';
import { clearSession, getAuthState, sessionStorageKey, setSession } from '../features/auth/session';
import { loginSession } from '../test/authFixtures';

beforeEach(() => clearSession());
afterEach(() => { clearSession(); vi.unstubAllGlobals(); });

describe('authenticated API client', () => {
  it('attaches the token to GET, POST and PATCH while preserving supplied Headers', async () => {
    const session = loginSession();
    setSession(session);
    const fetchMock = vi.fn<typeof fetch>().mockImplementation(async () => new Response('{}'));
    vi.stubGlobal('fetch', fetchMock);
    for (const method of ['GET', 'POST', 'PATCH'])
      await request('/tickets', { method, headers: new Headers({ 'X-Test': 'kept' }) });
    for (const [, init] of fetchMock.mock.calls) {
      const headers = new Headers(init?.headers);
      expect(headers.get('Authorization')).toBe(`Bearer ${session.accessToken}`);
      expect(headers.get('X-Test')).toBe('kept');
      expect(headers.get('Content-Type')).toBe('application/json');
    }
  });

  it('does not attach a token to login or invalidate another session for rejected credentials', async () => {
    const session = loginSession();
    setSession(session);
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(new Response('{"detail":"Invalid email or password."}', { status: 401 }));
    vi.stubGlobal('fetch', fetchMock);
    await expect(authApi.login({ email: session.user.email, password: 'wrong' })).rejects.toMatchObject({ status: 401 });
    expect(new Headers(fetchMock.mock.calls[0][1]?.headers).has('Authorization')).toBe(false);
    expect(getAuthState().session).toEqual(session);
  });

  it('clears memory and persisted credentials on a protected 401 even with an empty body', async () => {
    setSession(loginSession());
    vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockResolvedValue(new Response(null, { status: 401 })));
    await expect(request('/tickets')).rejects.toMatchObject({ status: 401 });
    expect(getAuthState().status).toBe('anonymous');
    expect(sessionStorage.getItem(sessionStorageKey)).toBeNull();
  });

  it.each([403, 500])('preserves the session on a %i response', async (status) => {
    const session = loginSession();
    setSession(session);
    vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockResolvedValue(new Response('{}', { status })));
    await expect(request('/tickets')).rejects.toMatchObject({ status });
    expect(getAuthState().session).toEqual(session);
  });

  it('does not let a delayed 401 from a previous session sign out the next agent', async () => {
    let finish!: (response: Response) => void;
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(() => new Promise((resolve) => { finish = resolve; })));
    setSession(loginSession({ accessToken: 'old-token' }));
    const pending = request('/tickets');
    const newSession = loginSession({ accessToken: 'new-token', user: { id: 2, fullName: 'Ben Osei', email: 'ben@example.test' } });
    setSession(newSession);
    finish(new Response(null, { status: 401 }));
    await expect(pending).rejects.toMatchObject({ status: 401 });
    expect(getAuthState().session).toEqual(newSession);
    expect(JSON.parse(sessionStorage.getItem(sessionStorageKey)!)).toEqual(newSession);
  });

  it('clears an expired token before sending another request', async () => {
    setSession(loginSession({ expiresAtUtc: new Date(Date.now() - 1000).toISOString() }));
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(new Response('{}'));
    vi.stubGlobal('fetch', fetchMock);
    await request('/tickets');
    expect(new Headers(fetchMock.mock.calls[0][1]?.headers).has('Authorization')).toBe(false);
    expect(getAuthState().status).toBe('anonymous');
  });
});
