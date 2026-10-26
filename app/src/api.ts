import { invoke } from "@tauri-apps/api/core";

/** One search result, as tb-core's `kb::Hit` sends it. */
export interface Hit {
  kind: string;
  title: string;
  subtitle: string;
  body: string;
  path: string;
}

export interface SearchResult {
  hits: Hit[];
  total: number;
  status: string;
}

export interface KbInfo {
  root: string;
  status: string;
  count: number;
  errors: string[];
}

/** Same kinds and order as SearchControl.cs. */
export const KINDS = ["ALL", "CODE", "PASSWORD", "MANUAL", "FILTER", "EQUIP", "NOTE", "FILE"] as const;

export const kbInfo = () => invoke<KbInfo>("kb_info");
export const kbSearch = (query: string, kind: string) =>
  invoke<SearchResult>("kb_search", { query, kind });
export const smokeMode = () => invoke<{ on: boolean; workOrders: boolean }>("smoke_mode");
export const smokeReport = (ok: boolean, detail: string) =>
  invoke<void>("smoke_report", { ok, detail });

/** One cheat sheet paragraph from assets/cheatsheet.rtf; tabs separate key and description. */
export interface Para {
  text: string;
  bold: boolean;
  big: boolean;
}

export interface Explained {
  text: string;
  kb: string | null;
}

/** FeatureBook row (status/reference text only). */
export interface SwitchRow {
  id: string;
  name: string;
  state: string;
  short: string;
  detail: string;
}

export const cheatsheet = () => invoke<Para[]>("cheatsheet");
export const explainCode = (spn: number, fmi: number) =>
  invoke<Explained>("explain_code", { spn, fmi });
export const guidanzFeatures = () => invoke<SwitchRow[]>("guidanz_features");

export interface WorkOrder {
  number: string;
  segment: string;
  customer: string;
  customerNo: string;
  model: string;
  serial: string;
  stock: string;
  description: string;
  assignedTech: string;
  notes: string;
  reportText: string;
  source: string;
  clockState: string;
  updatedUtc: string;
  folder: string;
  media: string[];
  clock: { kind: string; whenUtc: string; detail: string }[];
  /** Computed by tb-core: WO key, list label and clock lines exactly as the WinForms app shows them. */
  key: string;
  label: string;
  clockLines: string[];
}

export interface WoList {
  orders: WorkOrder[];
  conflicts: string[];
  techId: string;
  folder: string;
  /** Job-strip pick list from tb-core: "(no work order)" then each label. */
  pickRows: string[];
  syncChip: string;
}

export const woList = () => invoke<WoList>("wo_list");
export const woSave = (wo: WorkOrder) => invoke<WorkOrder>("wo_save", { wo });
export const woClock = (wo: WorkOrder, kind: "logon" | "logoff") =>
  invoke<WorkOrder>("wo_clock", { wo, kind });
export const woShare = (wo: WorkOrder) => invoke<{ wo: WorkOrder; dest: string }>("wo_share", { wo });
export const woAddPhotos = (wo: WorkOrder) =>
  invoke<{ wo: WorkOrder; count: number } | null>("wo_add_photos", { wo });
export const woOpenMedia = (folder: string, name: string) =>
  invoke<void>("wo_open_media", { folder, name });

/** The saved job in settings.json (shared with the WinForms app). */
export interface Job {
  customer: string;
  model: string;
  serial: string;
  lastWo: string;
}

export const jobGet = () => invoke<Job>("job_get");
export const jobSetHeader = (customer: string, model: string, serial: string) =>
  invoke<void>("job_set_header", { customer, model, serial });
export const jobSetWo = (key: string) => invoke<void>("job_set_wo", { key });
export const jobClear = () => invoke<void>("job_clear");
/** Job tag from tb-core; also sets the window title. */
export const jobTitle = (wo: WorkOrder | null, model: string, serial: string) =>
  invoke<string>("job_title", { wo, model, serial });

export interface Shop {
  techId: string;
  shareFolder: string;
  assignedFile: string;
  path: string;
}

export const shopSettingsGet = () => invoke<Shop>("shop_settings_get");
export const shopSettingsSave = (techId: string, shareFolder: string, assignedFile: string) =>
  invoke<Shop>("shop_settings_save", { techId, shareFolder, assignedFile });
export const pickFolder = () => invoke<string | null>("pick_folder");
export const pickAssignedFile = () => invoke<string | null>("pick_assigned_file");
export const kbOpen = (path: string) => invoke<void>("kb_open", { path });

/** Add code form (CodeEditForm): causes and checks are one entry per line. */
export interface CodeForm {
  brand: string;
  code: string;
  title: string;
  severity: string;
  controller: string;
  description: string;
  causes: string;
  checks: string;
  reset: string;
  safety: string;
}

export const kbCodeSeed = (kind: string, title: string) => invoke<CodeForm>("kb_code_seed", { kind, title });
/** Each add returns what to search for so the new entry shows straight away. */
export const kbAddCode = (code: CodeForm) => invoke<string>("kb_add_code", { code });
export const kbAddNote = (title: string, body: string) =>
  invoke<string>("kb_add_note", { title, body, stamp: localStamp() });
export const kbAddFile = () => invoke<string | null>("kb_add_file", { stamp: localStamp() });

/** Local time as yyyyMMdd_HHmmss (the shop note and file name stamp). */
export function localStamp(d = new Date()): string {
  const p = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}${p(d.getMonth() + 1)}${p(d.getDate())}_${p(d.getHours())}${p(d.getMinutes())}${p(d.getSeconds())}`;
}
