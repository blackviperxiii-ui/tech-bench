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
}

export const woList = () => invoke<WoList>("wo_list");
export const woSave = (wo: WorkOrder) => invoke<WorkOrder>("wo_save", { wo });
export const woClock = (wo: WorkOrder, kind: "logon" | "logoff") =>
  invoke<WorkOrder>("wo_clock", { wo, kind });
export const woShare = (wo: WorkOrder) => invoke<{ wo: WorkOrder; dest: string }>("wo_share", { wo });
