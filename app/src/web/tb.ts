import wasmUrl from "tb-wasm.wasm?url";

/** crates/tb-wasm's exports: one JSON call, the same shape as the RP1210 bridge's replies. */
interface Core {
  memory: WebAssembly.Memory;
  tb_alloc(len: number): number;
  tb_call(ptr: number, len: number): number;
  tb_out(): number;
}

let core: Promise<Core> | null = null;

function load(): Promise<Core> {
  core ??= fetch(wasmUrl)
    .then((r) => {
      if (!r.ok) throw new Error(`Could not load the Tech Bench core (${r.status}).`);
      return r.arrayBuffer();
    })
    .then((b) => WebAssembly.instantiate(b, {}))
    .then((m) => m.instance.exports as unknown as Core);
  return core;
}

/** One tb-core call. Rejects with the error text, as a Tauri command does. */
export async function tb<T>(cmd: string, args?: unknown): Promise<T> {
  const e = await load();
  const req = new TextEncoder().encode(JSON.stringify({ cmd, args: args ?? null }));
  const ptr = e.tb_alloc(req.length);
  new Uint8Array(e.memory.buffer, ptr, req.length).set(req);
  const len = e.tb_call(ptr, req.length);
  const reply = JSON.parse(new TextDecoder().decode(new Uint8Array(e.memory.buffer, e.tb_out(), len)));
  if (!reply.ok) throw String(reply.error);
  return reply.data as T;
}
