import type { LoginSession } from './types';

export const sessionStorageKey = 'northwind.auth.session';

export type AuthState =
  | { status: 'anonymous'; session: null; message?: string }
  | { status: 'restoring' | 'authenticated'; session: LoginSession }
  | { status: 'error'; session: LoginSession; message: string };

const listeners = new Set<() => void>();

function isSession(value: unknown): value is LoginSession {
  if (typeof value !== 'object' || value === null) return false;
  const candidate = value as Partial<LoginSession>;
  return typeof candidate.accessToken === 'string' && candidate.accessToken.length > 0
    && typeof candidate.expiresAtUtc === 'string' && Number.isFinite(Date.parse(candidate.expiresAtUtc))
    && typeof candidate.user === 'object' && candidate.user !== null
    && Number.isInteger(candidate.user.id) && candidate.user.id > 0
    && typeof candidate.user.fullName === 'string' && typeof candidate.user.email === 'string';
}

function readStoredState(): AuthState {
  try {
    const raw = sessionStorage.getItem(sessionStorageKey);
    if (raw) {
      const saved: unknown = JSON.parse(raw);
      if (isSession(saved) && Date.parse(saved.expiresAtUtc) > Date.now())
        return { status: 'restoring', session: saved };
      sessionStorage.removeItem(sessionStorageKey);
    }
  } catch {
    // A browser may block storage. The current session can still work in memory.
    try { sessionStorage.removeItem(sessionStorageKey); } catch { /* Storage may be blocked. */ }
  }
  return { status: 'anonymous', session: null };
}

let state: AuthState = readStoredState();

function publish(next: AuthState) {
  state = next;
  listeners.forEach((listener) => listener());
}

export function getAuthState(): AuthState { return state; }

export function subscribeToAuth(listener: () => void): () => void {
  listeners.add(listener);
  return () => { listeners.delete(listener); };
}

export function setSession(session: LoginSession) {
  try { sessionStorage.setItem(sessionStorageKey, JSON.stringify(session)); } catch { /* Memory-only session. */ }
  publish({ status: 'authenticated', session });
}

/** A response from an older session must never clear a newer login. */
export function clearSession(expectedToken?: string, message?: string) {
  if (expectedToken !== undefined && state.session?.accessToken !== expectedToken) return;
  try { sessionStorage.removeItem(sessionStorageKey); } catch { /* Storage may be blocked. */ }
  publish({ status: 'anonymous', session: null, message });
}

export function getAccessToken(): string | undefined {
  const session = state.session;
  if (session && Date.parse(session.expiresAtUtc) <= Date.now()) {
    clearSession(session.accessToken, 'Your session expired. Please sign in again.');
    return undefined;
  }
  return session?.accessToken;
}

export function finishRestoration(token: string, user: LoginSession['user']) {
  if (state.status === 'restoring' && state.session.accessToken === token)
    setSession({ ...state.session, user });
}

export function failRestoration(token: string, message: string) {
  if (state.status === 'restoring' && state.session.accessToken === token)
    publish({ status: 'error', session: state.session, message });
}

export function retryRestoration() {
  if (state.status === 'error') publish({ status: 'restoring', session: state.session });
}

/** Reloads the persisted session; used at initialization and for restoration verification. */
export function restoreStoredSession() { publish(readStoredState()); }
