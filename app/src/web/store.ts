/**
 * The web build's disk: IndexedDB in this browser. Work orders are kept as the desktop's wo.json
 * text under their packet folder name; the saved job is one record.
 */

const DB = "techbench";
const WO = "work-orders";
const JOB = "job";

export interface SavedJob {
  customer: string;
  model: string;
  serial: string;
  lastWo: string;
}

const NO_JOB: SavedJob = { customer: "", model: "", serial: "", lastWo: "" };

let db: Promise<IDBDatabase> | null = null;

function open(): Promise<IDBDatabase> {
  db ??= new Promise((resolve, reject) => {
    const r = indexedDB.open(DB, 1);
    r.onupgradeneeded = () => {
      r.result.createObjectStore(WO);
      r.result.createObjectStore(JOB);
    };
    r.onsuccess = () => resolve(r.result);
    r.onerror = () => reject(`This browser would not open its storage: ${r.error?.message ?? "unknown error"}`);
  });
  return db;
}

async function run<T>(store: string, mode: IDBTransactionMode, op: (s: IDBObjectStore) => IDBRequest<T>): Promise<T> {
  const t = (await open()).transaction(store, mode);
  const r = op(t.objectStore(store));
  return new Promise((resolve, reject) => {
    t.oncomplete = () => resolve(r.result);
    t.onerror = () => reject(`Could not save in this browser: ${t.error?.message ?? "unknown error"}`);
    t.onabort = t.onerror;
  });
}

/** Every saved wo.json text. */
export const packets = () => run<string[]>(WO, "readonly", (s) => s.getAll() as IDBRequest<string[]>);
export const putPacket = (id: string, text: string) => run(WO, "readwrite", (s) => s.put(text, id)).then(() => {});

export async function getJob(): Promise<SavedJob> {
  const j = await run<SavedJob | undefined>(JOB, "readonly", (s) => s.get(JOB) as IDBRequest<SavedJob | undefined>);
  return { ...NO_JOB, ...j };
}

export async function updateJob(change: Partial<SavedJob>): Promise<void> {
  const j = await getJob();
  await run(JOB, "readwrite", (s) => s.put({ ...j, ...change }, JOB));
}
