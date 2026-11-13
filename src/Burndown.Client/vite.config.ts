import { writeFileSync } from "node:fs";
import { resolve } from "node:path";
import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { defineConfig, type Plugin } from "vitest/config";

const backend = "http://127.0.0.1:9142";
const outDir = resolve(import.meta.dirname, "../Burndown.Web/wwwroot");

// emptyOutDir removes the tracked .gitkeep that keeps the folder in git.
const keepGitkeep = (): Plugin => ({
  name: "keep-gitkeep",
  apply: "build",
  closeBundle() {
    writeFileSync(resolve(outDir, ".gitkeep"), "");
  },
});

export default defineConfig({
  plugins: [react(), tailwindcss(), keepGitkeep()],
  build: {
    outDir,
    emptyOutDir: true,
    // Recharts makes the bundle larger than the default budget; it is served from localhost.
    chunkSizeWarningLimit: 800,
  },
  server: {
    host: "127.0.0.1",
    port: 5173,
    strictPort: true,
    proxy: {
      "/api": backend,
      "/health": backend,
    },
  },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./src/test-setup.ts"],
  },
});
