import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

const here = (p: string) => fileURLToPath(new URL(p, import.meta.url));
const version: string = JSON.parse(readFileSync(here("./src-tauri/tauri.conf.json"), "utf8")).version;

// The React UI lives in app/; src-tauri/ loads app/dist in release builds.
// `--mode web` is the browser build (app/dist-web): the Tauri API is swapped for app/src/web,
// which runs tb-core as wasm (crates/tb-wasm, built first by `npm run build:web`).
export default defineConfig(({ mode }) => {
  const web = mode === "web";
  return {
    root: "app",
    plugins: [react()],
    clearScreen: false,
    define: { __APP_VERSION__: JSON.stringify(version) },
    resolve: web
      ? {
          alias: [
            { find: "@tauri-apps/api/core", replacement: here("./app/src/web/core.ts") },
            { find: "@tauri-apps/api/event", replacement: here("./app/src/web/event.ts") },
            // A regex so the `?url` query survives the swap.
            { find: /^tb-wasm\.wasm/, replacement: here("./target/wasm32-unknown-unknown/release/tb_wasm.wasm") },
          ],
        }
      : undefined,
    server: { port: web ? 1421 : 1420, strictPort: true },
    preview: { port: 4173, strictPort: true },
    build: {
      outDir: web ? "dist-web" : "dist",
      emptyOutDir: true,
      target: "es2021",
    },
  };
});
