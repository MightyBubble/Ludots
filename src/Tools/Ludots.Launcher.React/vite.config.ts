import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tsconfigPaths from "vite-tsconfig-paths";

export default defineConfig(({ command }) => ({
  base: command === "build" ? "/launcher/" : "/",
  server: {
    host: "0.0.0.0",
    port: 5174,
    strictPort: true,
  },
  plugins: [react(), tsconfigPaths()],
}));
