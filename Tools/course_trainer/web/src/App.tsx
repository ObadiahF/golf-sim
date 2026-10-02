import { Login } from './Login';
import { TrainerView } from './TrainerView';
import { useSession } from './useSession';

/** Login gate: the trainer (and its first API calls) only mounts once there is a session. */
export default function App() {
  const session = useSession();
  const { session: s, login } = session;
  if (s.state === 'checking') return <div className="login-backdrop" />;
  if (s.state === 'out') return <Login onLogin={login} />;
  return <TrainerView key={s.name} session={session} name={s.name} />;
}
