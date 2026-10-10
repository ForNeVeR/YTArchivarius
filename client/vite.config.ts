// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

const backend = 'https://localhost:5001';

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../YTArchivarius/wwwroot',
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': { target: backend, secure: false },
      '/auth': { target: backend, secure: false },
    },
  },
});
