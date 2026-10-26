import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// The React UI lives in app/; src-tauri/ loads app/dist in release builds.
export default defineConfig({
  root: "app",
  plugins: [react()],
  clearScreen: false,
  server: { port: 1420, strictPort: true },
  build: { outDir: "dist", emptyOutDir: true, target: "es2021" },
});
