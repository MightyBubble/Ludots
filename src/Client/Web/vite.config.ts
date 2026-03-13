import { defineConfig } from 'vite';
import { resolve } from 'path';

export default defineConfig({
  resolve: {
    alias: {
      '@': resolve(__dirname, 'src'),
    },
  },
  test: {
    environment: 'node',
  },
  build: {
    outDir: 'dist',
  },
  server: {
    proxy: {
      '/ws': {
        target: 'ws://localhost:5200',
        ws: true,
      },
      '/health': {
        target: 'http://localhost:5200',
      },
    },
  },
});
