import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// `npm run dev` serves the UI on :5173 and forwards /api to the Python server (python server.py).
const API = process.env.TRAINER_API ?? 'http://127.0.0.1:8765';

export default defineConfig({
  plugins: [react()],
  server: { proxy: { '/api': API } },
  build: { chunkSizeWarningLimit: 1500 },
});
