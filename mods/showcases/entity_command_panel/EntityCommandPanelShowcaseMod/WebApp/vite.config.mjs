import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

export default defineConfig({
  base: './',
  plugins: [react()],
  build: {
    outDir: '../assets/entity-command-panel-app',
    emptyOutDir: true,
    sourcemap: false,
    chunkSizeWarningLimit: 900
  }
});
