import { useCallback, useEffect, useState } from 'react';
import { api, UNAUTHORIZED } from './api';

export type SessionState = { state: 'checking' } | { state: 'out' } | { state: 'in'; name: string };

/** Who is logged in (the HttpOnly cookie is invisible to JS, so ask the server), plus login / logout. */
export function useSession() {
  const [session, setSession] = useState<SessionState>({ state: 'checking' });

  useEffect(() => {
    api.me().then(me => setSession({ state: 'in', name: me.name }), () => setSession({ state: 'out' }));
    const onUnauthorized = () => setSession({ state: 'out' });
    window.addEventListener(UNAUTHORIZED, onUnauthorized);
    return () => window.removeEventListener(UNAUTHORIZED, onUnauthorized);
  }, []);

  const login = useCallback(async (name: string, password: string) => {
    const me = await api.login(name, password);
    setSession({ state: 'in', name: me.name });
  }, []);

  const logout = useCallback(async () => {
    try { await api.logout(); } finally { setSession({ state: 'out' }); }
  }, []);

  return { session, login, logout };
}

export type Session = ReturnType<typeof useSession>;
