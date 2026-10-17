import type { AuthenticatedAgent, LoginRequest, LoginSession } from '../features/auth/types';
import { request } from './client';

export const authApi = {
  login: (credentials: LoginRequest) => request<LoginSession>('/auth/login', {
    method: 'POST', body: JSON.stringify(credentials),
  }, { anonymous: true }),
  me: () => request<AuthenticatedAgent>('/auth/me'),
};
