import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { useAuth } from './useAuth';

export function ProtectedRoute() {
  const auth = useAuth();
  const location = useLocation();
  return auth.status === 'authenticated' ? <Outlet /> : <Navigate to="/login" replace
    state={{ from: `${location.pathname}${location.search}${location.hash}` }} />;
}
