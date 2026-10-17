import type { LoginSession } from '../features/auth/types';

export function loginSession(overrides: Partial<LoginSession> = {}): LoginSession {
  return {
    accessToken: 'test-access-token',
    expiresAtUtc: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
    user: { id: 1, fullName: 'Alex Turner', email: 'alex.turner@northwind-support.example' },
    ...overrides,
  };
}
