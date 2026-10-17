import { useEffect } from 'react';
import { Outlet } from 'react-router-dom';
import { authApi } from '../../api/auth';
import { toErrorMessage } from '../../api/client';
import { Spinner } from '../../components/Spinner';
import { clearSession, failRestoration, finishRestoration, retryRestoration } from './session';
import { useAuth } from './useAuth';

/** Validates persisted sessions before mounting any protected page. */
export function AuthRoot() {
  const auth = useAuth();

  useEffect(() => {
    if (auth.status !== 'restoring') return;
    let cancelled = false;
    const token = auth.session.accessToken;
    void authApi.me().then((user) => {
      if (!cancelled) finishRestoration(token, user);
    }).catch((error: unknown) => {
      if (!cancelled) failRestoration(token, toErrorMessage(error, 'Could not verify your session.'));
    });
    return () => { cancelled = true; };
  }, [auth]);

  useEffect(() => {
    if (!auth.session) return;
    const { accessToken, expiresAtUtc } = auth.session;
    const expire = () => clearSession(accessToken, 'Your session expired. Please sign in again.');
    const remaining = Date.parse(expiresAtUtc) - Date.now();
    const timer = window.setTimeout(expire, Math.min(Math.max(remaining, 0), 2_147_483_647));
    const checkOnFocus = () => { if (Date.parse(expiresAtUtc) <= Date.now()) expire(); };
    window.addEventListener('focus', checkOnFocus);
    return () => { window.clearTimeout(timer); window.removeEventListener('focus', checkOnFocus); };
  }, [auth]);

  if (auth.status === 'restoring')
    return <main className="app__main"><Spinner label="Checking your session" /></main>;

  if (auth.status === 'error') return (
    <main className="app__main auth-page">
      <section className="card" aria-labelledby="session-error-title">
        <h1 id="session-error-title">Could not verify your session</h1>
        <p role="alert">{auth.message}</p>
        <div className="auth-actions">
          <button className="button button--primary" onClick={retryRestoration}>Retry</button>
          <button className="button" onClick={() => clearSession()}>Return to login</button>
        </div>
      </section>
    </main>
  );

  return <Outlet />;
}
