// Typed client for the Python API (Tools/course_trainer/api.py).

export type Rating = 'up' | 'down';

export interface Preset { name: string; label: string; theme: string }
export interface FeedbackTag { id: string; label: string; knobs: Record<string, number> }
export interface Catalog {
  presets: Preset[];
  params: Record<string, string>;
  pars: number[];
  feedback: FeedbackTag[];
}

export interface HoleSummary {
  id: string;
  preset: string;
  theme: string;
  par: number;
  seed: number;
  params: Record<string, number>;
  modelScore: number | null;
  lengthMeters: number;
  created: number;
  rating: Rating | null;
}

export interface Status {
  ratings: number;
  up: number;
  trainedOn: number;
  preset: string | null;
  likes: string[];
  dislikes: string[];
  /** How many of `ratings` are the logged-in user's. */
  yours: number;
}

export interface Session { name: string }

/** Fired on any 401: the session expired or was revoked, so the app shows the login screen again. */
export const UNAUTHORIZED = 'trainer:unauthorized';

/** Mirror of hole.json (Docs/hole-format, version 2). Points are flat [x0, y0, x1, y1, ...] in local meters, y = north. */
export interface PointList { points: number[] }
export interface HoleArea { surface: string; sourceId?: string; rings: PointList[] }
export interface WaterBody { level: number; triangles: PointList }
export interface XZ { x: number; y: number }
export interface HolePackage {
  version: 2;
  id: string;
  course: string;
  holeRef: string;
  par: number;
  theme: string;
  sizeMeters: number;
  heightmap: { file: string; resolution: number; minElevation: number; maxElevation: number };
  holePath: PointList;
  tee: XZ;
  pin: XZ;
  areas: HoleArea[];
  water: WaterBody[];
  objects: { file: string; count: number };
}

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, init);
  if (res.status === 401 && url !== '/api/login') window.dispatchEvent(new Event(UNAUTHORIZED));
  if (!res.ok) {
    let detail = res.statusText;
    try { detail = (await res.json()).detail ?? detail; } catch { /* not JSON */ }
    throw new Error(`${res.status}: ${typeof detail === 'string' ? detail : JSON.stringify(detail)}`);
  }
  return res.json() as Promise<T>;
}

const post = <T>(url: string, body?: unknown) =>
  request<T>(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body ?? {}) });

export const holeFileUrl = (id: string, name: string) => `/api/holes/${encodeURIComponent(id)}/${name}`;

export const api = {
  me: () => request<Session>('/api/me'),
  login: (name: string, password: string) => post<Session>('/api/login', { name, password }),
  logout: () => post<{ ok: boolean }>('/api/logout'),
  hole: (id: string) => request<HoleSummary>(`/api/holes/${encodeURIComponent(id)}`),
  catalog: () => request<Catalog>('/api/presets'),
  status: (preset?: string) => request<Status>(`/api/status${preset ? `?preset=${preset}` : ''}`),
  recent: (limit = 20) => request<{ holes: HoleSummary[] }>(`/api/holes?limit=${limit}`).then(r => r.holes),
  generate: (preset: string, par: number | null) => post<HoleSummary>('/api/generate', { preset, par }),
  rate: (id: string, rating: Rating, comment: string, tags: string[]) =>
    post<{ status: Status }>('/api/rate', { id, rating, comment, tags }),
  train: (preset?: string) => post<Status>(`/api/train${preset ? `?preset=${preset}` : ''}`),
  packageJson: (id: string) => request<HolePackage>(holeFileUrl(id, 'hole.json')),
  /** A binary package file (heightmap.raw, objects.bin). */
  binary: async (id: string, name: string) => {
    const res = await fetch(holeFileUrl(id, name));
    if (!res.ok) throw new Error(`${name}: ${res.status}`);
    return res.arrayBuffer();
  },
};
