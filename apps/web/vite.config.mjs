import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  server: {
    host: "127.0.0.1",
    port: 4173,
    strictPort: true,
  },
  preview: {
    host: "127.0.0.1",
    port: 4173,
    strictPort: true,
  },
  build: {
    outDir: "dist",
    sourcemap: true,
    // The module-preload polyfill is a fallback for browsers without native `modulepreload`, and every
    // browser this workspace targets has it. Shipping the fallback would spend bytes on code that never runs.
    modulePreload: { polyfill: false },
  },
});
