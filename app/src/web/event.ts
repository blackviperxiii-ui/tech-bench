// Stands in for @tauri-apps/api/event in the web build: the browser has no shop watcher.
export type UnlistenFn = () => void;

export async function listen(_event: string, _handler: unknown): Promise<UnlistenFn> {
  return () => {};
}
