import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

export default defineConfig({
  base: './',
  plugins: [react()],
  build: {
    outDir: '../Assets/sango-app',
    emptyOutDir: true,
    sourcemap: false,
    chunkSizeWarningLimit: 2000
  }
});
