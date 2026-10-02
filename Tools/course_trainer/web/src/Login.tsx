import { useState, type FormEvent } from 'react';

/** Name + password card on the clubhouse-green backdrop. No sign-up: accounts are made by the admin. */
export function Login({ onLogin }: { onLogin: (name: string, password: string) => Promise<void> }) {
  const [name, setName] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await onLogin(name.trim(), password);
    } catch (err) {
      setError((err instanceof Error ? err.message : String(err)).replace(/^\d+: /, ''));
      setBusy(false);
    }
  };

  return (
    <div className="login-backdrop">
      <form className="card login" onSubmit={submit}>
        <header className="card-head">
          <span className="eyebrow">Course Trainer</span>
          <span className="flag" aria-hidden>⛳</span>
        </header>
        <p className="muted">Sign in to walk, rate and retrain generated holes.</p>
        <label>
          <small>Name</small>
          <input autoFocus autoComplete="username" value={name} onChange={e => setName(e.target.value)} required />
        </label>
        <label>
          <small>Password</small>
          <input type="password" autoComplete="current-password" value={password}
                 onChange={e => setPassword(e.target.value)} required />
        </label>
        {error && <div className="login-error" role="alert">{error}</div>}
        <button className="btn submit" disabled={busy || !name.trim() || !password}>
          {busy ? 'Signing in…' : 'Sign in'}
        </button>
      </form>
    </div>
  );
}
