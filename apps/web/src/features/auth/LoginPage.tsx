import { useEffect, useRef, useState } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { authApi } from '../../api/auth';
import { ApiError, toErrorMessage } from '../../api/client';
import { Field } from '../../components/Field';
import { setSession } from './session';
import { useAuth } from './useAuth';
import { returnTo } from './returnTo';

export function LoginPage() {
  const auth = useAuth();
  const location = useLocation();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [error, setError] = useState<string>();
  const [pending, setPending] = useState(false);
  const submitting = useRef(false);
  const attempt = useRef(0);
  useEffect(() => () => { attempt.current += 1; }, []);

  if (auth.status === 'authenticated') return <Navigate to={returnTo(location.state)} replace />;

  const submit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (submitting.current) return;
    const normalizedEmail = email.trim();
    const validation: Record<string, string> = {};
    if (normalizedEmail.length > 256 || !/^[^\s@]+@[^\s@]+$/.test(normalizedEmail))
      validation.email = 'Enter a valid email address.';
    if (!password.trim() || password.length > 1024) validation.password = 'Enter your password (up to 1024 characters).';
    setErrors(validation);
    setError(undefined);
    if (Object.keys(validation).length) return;

    submitting.current = true;
    setPending(true);
    const id = ++attempt.current;
    try {
      const session = await authApi.login({ email: normalizedEmail, password });
      if (id === attempt.current) { setPassword(''); setSession(session); }
    } catch (caught) {
      if (id !== attempt.current) return;
      if (caught instanceof ApiError) setErrors(Object.fromEntries(Object.entries(caught.fieldErrors)
        .map(([field, messages]) => [field.charAt(0).toLowerCase() + field.slice(1), messages[0]])));
      setError(toErrorMessage(caught, 'Could not sign in. Please try again.'));
    } finally {
      if (id === attempt.current) { submitting.current = false; setPending(false); }
    }
  };

  return (
    <main className="app__main auth-page">
      <section className="card" aria-labelledby="login-title">
        <p className="app__brand">Northwind Support</p>
        <h1 id="login-title">Sign in</h1>
        <p>Use your support agent email and password.</p>
        {auth.status === 'anonymous' && auth.message && <p role="status">{auth.message}</p>}
        <form className="form" noValidate aria-busy={pending} onSubmit={(event) => void submit(event)}>
          <fieldset className="escalation-fields" disabled={pending}>
            <legend className="visually-hidden">Agent credentials</legend>
            <Field id="login-email" label="Email" error={errors.email}>
              {(props) => <input {...props} type="email" name="email" autoComplete="username"
                value={email} onChange={(event) => setEmail(event.target.value)} required />}
            </Field>
            <Field id="login-password" label="Password" error={errors.password}>
              {(props) => <input {...props} type="password" name="password" autoComplete="current-password"
                value={password} onChange={(event) => setPassword(event.target.value)} required />}
            </Field>
            <button className="button button--primary" type="submit">{pending ? 'Signing in...' : 'Sign in'}</button>
          </fieldset>
        </form>
        {error && <p className="error" role="alert">{error}</p>}
      </section>
    </main>
  );
}
