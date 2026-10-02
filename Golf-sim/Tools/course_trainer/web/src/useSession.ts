import { useCallback, useEffect, useRef, useState } from 'react';
import { api, endSessionRequests, UNAUTHORIZED } from './api';

export type SessionState = { state: 'checking' } | { state: 'out' } | { state: 'in'; name: string };

/** Who is logged in (the HttpOnly cookie is invisible to JS, so ask the server), plus login / logout. */
export function useSession() {
  const [session, setSession] = useState<SessionState>({ state: 'checking' });
  const signedIn = useRef(false);

  /** A session ended (logout, or any 401): cancel its requests and forget its per-user state. TrainerView
   *  unmounts with it; the URL's ?hole= is the one thing that would survive, so it goes too (a shared link opened
   *  while logged out is kept: there was no session to end). */
  const end = useCallback(() => {
    endSessionRequests();
    if (signedIn.current) window.history.replaceState(null, '', window.location.pathname);
    signedIn.current = false;
    setSession({ state: 'out' });
  }, []);

  const start = useCallback((name: string) => {
    signedIn.current = true;
    setSession({ state: 'in', name });
  }, []);

  useEffect(() => {
    api.me().then(me => start(me.name), () => setSession({ state: 'out' }));
    window.addEventListener(UNAUTHORIZED, end);
    return () => window.removeEventListener(UNAUTHORIZED, end);
  }, [start, end]);

  const login = useCallback(async (name: string, password: string) => {
    const me = await api.login(name, password);
    start(me.name);
  }, [start]);

  const logout = useCallback(async () => {
    endSessionRequests();  // before logging out: nothing in flight may land after it
    try { await api.logout(); } finally { end(); }
  }, [end]);

  return { session, login, logout };
}

export type Session = ReturnType<typeof useSession>;
