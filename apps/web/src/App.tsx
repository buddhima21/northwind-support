import { Link, NavLink, Outlet, useNavigate } from 'react-router-dom';
import { clearSession } from './features/auth/session';
import { useAuth } from './features/auth/useAuth';

/** Application shell: the header, the navigation, and wherever the router puts the page. */
export function App() {
  const auth = useAuth();
  const navigate = useNavigate();
  const logout = () => { clearSession(); void navigate('/login', { replace: true, state: null }); };
  return (
    <div className="app">
      <header className="app__header">
        <Link className="app__brand" to="/tickets">
          Northwind Support
        </Link>

        <nav className="app__nav" aria-label="Main">
          <NavLink to="/tickets">Tickets</NavLink>
          <NavLink to="/customers">Customers</NavLink>
        </nav>
        <div className="app__session">
          <span>Signed in as {auth.session?.user.fullName}</span>
          <button className="button" onClick={logout}>Log out</button>
        </div>
      </header>

      <main className="app__main">
        <Outlet />
      </main>
    </div>
  );
}
