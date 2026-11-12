import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tsconfigPaths from "vite-tsconfig-paths";
import { traeBadgePlugin } from 'vite-plugin-trae-solo-badge';

// https://vite.dev/config/
const editorPort = Number(process.env.LUDOTS_EDITOR_PORT || 5173);
const editorBridge = process.env.LUDOTS_EDITOR_BRIDGE_URL || 'http://localhost:5299';

export default defineConfig({
  server: {
    host: 'localhost',
    port: editorPort,
    strictPort: true,
    proxy: {
      '/api': {
        target: editorBridge,
        changeOrigin: true,
      },
      '/health': {
        target: editorBridge,
        changeOrigin: true,
      },
      '/agent-bridge': {
        target: 'http://127.0.0.1:47921',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/agent-bridge/, ''),
      },
    },
  },
  build: {
    sourcemap: 'hidden',
  },
  plugins: [
    react({
      babel: {
        plugins: [
          'react-dev-locator',
        ],
      },
    }),
    traeBadgePlugin({
      variant: 'dark',
      position: 'bottom-right',
      prodOnly: true,
      clickable: true,
      clickUrl: 'https://www.trae.ai/solo?showJoin=1',
      autoTheme: true,
      autoThemeTarget: '#root'
    }), 
    tsconfigPaths()
  ],
})
