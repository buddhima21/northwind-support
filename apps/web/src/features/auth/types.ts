export interface AuthenticatedAgent {
  id: number;
  fullName: string;
  email: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginSession {
  accessToken: string;
  expiresAtUtc: string;
  user: AuthenticatedAgent;
}
