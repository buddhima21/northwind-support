import { StrictMode } from 'react';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from '../../api/client';
import { routes } from '../../routes';
import { loginSession } from '../../test/authFixtures';
import { page, ticketDetail } from '../../test/fixtures';
import { clearSession, getAuthState, restoreStoredSession, sessionStorageKey, setSession } from './session';

const fetchMock = vi.fn<typeof fetch>();
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
let router: ReturnType<typeof createMemoryRouter> | undefined;

beforeEach(() => {
  clearSession();
  fetchMock.mockReset();
  fetchMock.mockImplementation(async (input) => {
    const url = String(input);
    if (url === '/api/auth/login') return json(loginSession());
    if (url === '/api/auth/me') return json(loginSession().user);
    if (url === '/api/tickets/12') return json(ticketDetail());
    if (url.startsWith('/api/tickets?')) return json(page([]));
    if (['/api/customers', '/api/categories', '/api/agents', '/api/tickets/12/escalations'].includes(url)) return json([]);
    throw new Error(`Unexpected request: ${url}`);
  });
  vi.stubGlobal('fetch', fetchMock);
});

afterEach(() => {
  cleanup();
  router?.dispose();
  clearSession();
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

function open(path: string, state?: unknown) {
  router = createMemoryRouter(routes, { initialEntries: [{ pathname: path.split(/[?#]/)[0],
    search: path.includes('?') ? `?${path.split('?')[1].split('#')[0]}` : '',
    hash: path.includes('#') ? `#${path.split('#')[1]}` : '', state }] });
  render(<StrictMode><RouterProvider router={router} /></StrictMode>);
  return router;
}

function credentials(email = 'alex.turner@northwind-support.example', password = 'Local password!') {
  fireEvent.change(screen.getByLabelText('Email'), { target: { value: email } });
  fireEvent.change(screen.getByLabelText('Password'), { target: { value: password } });
}

describe('authentication routing and login', () => {
  it('redirects a deep link to login, then returns to it with the signed-in user and authenticated requests', async () => {
    const navigation = open('/tickets/12?view=history#escalations');
    await screen.findByRole('heading', { name: 'Sign in' });
    expect(fetchMock).not.toHaveBeenCalled();
    credentials('  alex.turner@northwind-support.example  ', ' Keep spaces! ');
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    await screen.findByRole('heading', { name: ticketDetail().title });
    expect(navigation.state.location.pathname).toBe('/tickets/12');
    expect(navigation.state.location.search).toBe('?view=history');
    expect(navigation.state.location.hash).toBe('#escalations');
    expect(screen.getByText('Signed in as Alex Turner')).toBeInTheDocument();
    const login = fetchMock.mock.calls.find(([url]) => url === '/api/auth/login')!;
    expect(JSON.parse(String(login[1]?.body))).toEqual({ email: 'alex.turner@northwind-support.example', password: ' Keep spaces! ' });
    expect(new Headers(login[1]?.headers).has('Authorization')).toBe(false);
    for (const [, init] of fetchMock.mock.calls.filter(([url]) => url !== '/api/auth/login'))
      expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer test-access-token');
    expect(sessionStorage.getItem(sessionStorageKey)).not.toContain('Keep spaces');
  });

  it('validates accessible fields before requesting and shows rejected credentials without creating a session', async () => {
    open('/login');
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(screen.getByLabelText('Email')).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByLabelText('Password')).toHaveAttribute('aria-invalid', 'true');
    expect(fetchMock).not.toHaveBeenCalled();
    fetchMock.mockResolvedValue(json({ detail: 'Invalid email or password.' }, 401));
    credentials();
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Invalid email or password.');
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeEnabled();
    expect(getAuthState().session).toBeNull();
    expect(sessionStorage.getItem(sessionStorageKey)).toBeNull();
  });

  it('blocks duplicate submissions while login is pending and shows a retryable network failure', async () => {
    let fail!: (reason: Error) => void;
    fetchMock.mockImplementation(() => new Promise((_, reject) => { fail = reject; }));
    open('/login');
    credentials();
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(screen.getByRole('button', { name: 'Signing in...' })).toBeDisabled();
    expect(screen.getByLabelText('Email')).toBeDisabled();
    fireEvent.submit(screen.getByRole('button', { name: 'Signing in...' }).closest('form')!);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    await act(async () => { fail(new Error('Network offline')); });
    expect(await screen.findByRole('alert')).toHaveTextContent('Could not sign in.');
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeEnabled();
  });

  it('uses the ticket list for a direct login and rejects an external return path', async () => {
    const navigation = open('/login', { from: '//evil.example' });
    credentials();
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    await screen.findByRole('heading', { name: 'Tickets' });
    expect(navigation.state.location.pathname).toBe('/tickets');
  });

  it('logs out, clears storage, and prevents returning to a protected page', async () => {
    setSession(loginSession());
    const navigation = open('/tickets/12');
    await screen.findByRole('heading', { name: ticketDetail().title });
    await userEvent.click(screen.getByRole('button', { name: 'Log out' }));
    await screen.findByRole('heading', { name: 'Sign in' });
    expect(sessionStorage.getItem(sessionStorageKey)).toBeNull();
    await act(async () => { await navigation.navigate('/tickets/12'); });
    await waitFor(() => expect(navigation.state.location.pathname).toBe('/login'));
    expect(screen.queryByText(ticketDetail().title)).not.toBeInTheDocument();
  });

  it('a 401 during any API call removes the protected screen and returns to login', async () => {
    setSession(loginSession());
    const navigation = open('/tickets/12');
    await screen.findByRole('heading', { name: ticketDetail().title });
    fetchMock.mockResolvedValue(new Response(null, { status: 401 }));
    await act(async () => { await expect(request('/categories')).rejects.toMatchObject({ status: 401 }); });
    await screen.findByRole('heading', { name: 'Sign in' });
    expect(navigation.state.location.pathname).toBe('/login');
    expect(sessionStorage.getItem(sessionStorageKey)).toBeNull();
    expect(screen.queryByText(ticketDetail().title)).not.toBeInTheDocument();
  });

  it('expires the mounted session without waiting for another API call', async () => {
    setSession(loginSession());
    open('/tickets');
    await screen.findByRole('heading', { name: 'Tickets' });
    vi.useFakeTimers();
    act(() => setSession(loginSession({ expiresAtUtc: new Date(Date.now() + 1000).toISOString() })));
    await act(async () => { vi.advanceTimersByTime(1000); });
    expect(screen.getByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
    expect(sessionStorage.getItem(sessionStorageKey)).toBeNull();
  });
});

describe('session restoration after refresh', () => {
  function savedSession() {
    sessionStorage.setItem(sessionStorageKey, JSON.stringify(loginSession()));
    restoreStoredSession();
  }

  it('verifies /me before loading protected data and replaces saved profile details with server data', async () => {
    savedSession();
    let complete!: (value: Response) => void;
    const defaultFetch = fetchMock.getMockImplementation()!;
    fetchMock.mockImplementation((url, init) => String(url) === '/api/auth/me'
      ? new Promise((resolve) => { complete = resolve; }) : defaultFetch(url, init));
    open('/tickets');
    expect(screen.getByRole('status')).toHaveTextContent('Checking your session');
    expect(fetchMock.mock.calls.every(([url]) => url === '/api/auth/me')).toBe(true);
    await act(async () => { complete(json({ ...loginSession().user, fullName: 'Updated Alex' })); });
    await screen.findByRole('heading', { name: 'Tickets' });
    expect(screen.getByText('Signed in as Updated Alex')).toBeInTheDocument();
  });

  it('clears a rejected persisted session and redirects to login', async () => {
    savedSession();
    fetchMock.mockImplementation(async () => new Response(null, { status: 401 }));
    open('/customers');
    await screen.findByRole('heading', { name: 'Sign in' });
    expect(getAuthState().session).toBeNull();
    expect(sessionStorage.getItem(sessionStorageKey)).toBeNull();
  });

  it('preserves credentials for a temporary server failure and restores the session on retry', async () => {
    savedSession();
    const defaultFetch = fetchMock.getMockImplementation()!;
    let rejectMe = true;
    fetchMock.mockImplementation(async (url, init) => rejectMe && String(url) === '/api/auth/me'
      ? json({ detail: 'Service unavailable' }, 503) : defaultFetch(url, init));
    open('/tickets');
    expect(await screen.findByRole('alert')).toHaveTextContent('Service unavailable');
    expect(sessionStorage.getItem(sessionStorageKey)).not.toBeNull();
    expect(screen.queryByRole('heading', { name: 'Tickets' })).not.toBeInTheDocument();
    rejectMe = false;
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
    await screen.findByRole('heading', { name: 'Tickets' });
  });

  it('does not restore an older user after the session has been replaced', async () => {
    savedSession();
    let complete!: (value: Response) => void;
    const defaultFetch = fetchMock.getMockImplementation()!;
    fetchMock.mockImplementation((url, init) => String(url) === '/api/auth/me'
      ? new Promise((resolve) => { complete = resolve; }) : defaultFetch(url, init));
    open('/tickets');
    const replacement = loginSession({ accessToken: 'ben-token', user: { id: 2, fullName: 'Ben Osei', email: 'ben@example.test' } });
    act(() => setSession(replacement));
    await screen.findByText('Signed in as Ben Osei');
    await act(async () => { complete(json(loginSession().user)); });
    expect(getAuthState().session).toEqual(replacement);
    expect(screen.getByText('Signed in as Ben Osei')).toBeInTheDocument();
  });
});
