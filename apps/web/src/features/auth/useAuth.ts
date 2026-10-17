import { useSyncExternalStore } from 'react';
import { getAuthState, subscribeToAuth } from './session';

export function useAuth() {
  return useSyncExternalStore(subscribeToAuth, getAuthState, getAuthState);
}
