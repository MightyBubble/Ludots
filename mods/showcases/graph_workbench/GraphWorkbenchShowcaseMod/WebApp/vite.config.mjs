import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  base: './',
  build: {
    outDir: '../Assets/graph-workbench-app',
    emptyOutDir: true
  }
});
