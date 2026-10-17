/** Only allow a local path, and never send a successful login back into /login. */
export function returnTo(locationState: unknown): string {
  if (typeof locationState !== 'object' || locationState === null || !('from' in locationState)) return '/tickets';
  const candidate = locationState.from;
  if (typeof candidate !== 'string' || !candidate.startsWith('/') || candidate.startsWith('//')
    || candidate.includes('\\')) return '/tickets';
  try {
    const url = new URL(candidate, window.location.origin);
    const path = decodeURIComponent(url.pathname).replace(/\/+$/, '').toLowerCase();
    return url.origin === window.location.origin && path !== '/login' ? candidate : '/tickets';
  } catch {
    return '/tickets';
  }
}
