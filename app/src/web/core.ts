// Stands in for @tauri-apps/api/core in the web build (vite.config.ts aliases it).
import { webInvoke } from "./backend";

export function invoke<T>(cmd: string, args?: Record<string, unknown>): Promise<T> {
  return webInvoke(cmd, args ?? {}) as Promise<T>;
}
