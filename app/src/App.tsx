import { listen } from "@tauri-apps/api/event";
import { useCallback, useEffect, useRef, useState } from "react";
import { CheatSheet } from "./CheatSheet";
import { Inline7 } from "./Inline7";
import { JobStrip, type Header } from "./JobStrip";
import { AddCode, AddNote } from "./KbAdd";
import { Search } from "./Search";
import { ShopSettings } from "./ShopSettings";
import { ShopSync } from "./ShopSync";
import { WorkOrders } from "./WorkOrders";
import { runSmoke } from "./smoke";
import { WEB } from "./platform";
import {
  jobClear,
  jobGet,
  jobSetHeader,
  jobSetWo,
  jobTitle,
  kbAddFile,
  smokeMode,
  syncRun,
  woAttachReport,
  woList,
  type SyncView,
  type Hit,
  type WoList,
  type WorkOrder,
} from "./api";

/** Same main pages as ShellForm; screens not ported yet say so. */
const ALL_PAGES = ["Search", "Work orders", "INLINE 7", "Settings"] as const;
type Page = (typeof ALL_PAGES)[number];
/** The web build has no shop settings: nothing to share with and no folders to pick. */
const PAGES = WEB ? ALL_PAGES.filter((p) => p !== "Settings") : ALL_PAGES;

const NO_HEADER: Header = { customer: "", model: "", serial: "" };

export function App() {
  const [page, setPage] = useState<Page>("Search");
  const [help, setHelp] = useState(false);
  const [focusTick, setFocusTick] = useState(0);
  const [prefill, setPrefill] = useState({ text: "", tick: 0 });
  const [data, setData] = useState<WoList | null>(null);
  const [current, setCurrent] = useState<WorkOrder | null>(null);
  const [header, setHeader] = useState<Header>(NO_HEADER);
  const loaded = useRef(false);
  const [hit, setHit] = useState<Hit | null>(null);
  const [adding, setAdding] = useState<"code" | "note" | null>(null);
  const [kbTick, setKbTick] = useState(0);
  const [addError, setAddError] = useState("");
  const [sync, setSync] = useState<SyncView | null>(null);
  const [syncOpen, setSyncOpen] = useState(false);
  const [jobTag, setJobTag] = useState("");
  const [syncCount, setSyncCount] = useState(0);
  /** A finished sync: update the chip, and let pages that read synced files look again. */
  const synced = useCallback((v: SyncView) => {
    setSync(v);
    setSyncCount((c) => c + 1);
  }, []);
  /** Smoke runs never sync: the installed KB stays untouched. */
  const smokeOn = useRef(true);

  /** Re-list work orders; with `key`, select that order when it is listed. */
  const reload = useCallback(async (key?: string) => {
    const d = await woList();
    setData(d);
    if (key) {
      const hit = d.orders.find((o) => o.key.toUpperCase() === key.toUpperCase());
      if (hit) setCurrent(hit);
    }
  }, []);

  /** ShellForm.RunSync(false): a quiet sync; a failure shows on the chip, not in a dialog. */
  const quietSync = useCallback(() => {
    if (smokeOn.current || WEB) return;
    syncRun()
      .then(synced)
      .catch((e) => setSync({ techId: "", syncFolder: "", text: String(e), conflicts: [], chip: String(e) }));
  }, [synced]);

  // Startup: saved job (customer / model / serial), then the list with the last work order open,
  // then one quiet sync (ShellForm.Shown).
  useEffect(() => {
    smokeMode()
      .then((m) => {
        smokeOn.current = m.on;
        if (m.on) void runSmoke(m.workOrders);
        else quietSync();
      })
      .catch(() => {
        /* Not running inside Tauri (plain vite dev): no smoke mode. */
      });
    jobGet()
      .then(async (j) => {
        setHeader({ customer: j.customer, model: j.model, serial: j.serial });
        await reload(j.lastWo);
      })
      .catch(() => {
        /* Plain vite dev. */
      })
      .finally(() => {
        loaded.current = true;
      });
  }, [reload, quietSync]);

  // ShopWatcher: the shell polls the shop folders and says when they settle after a change.
  useEffect(() => {
    let stop: (() => void) | undefined;
    let gone = false;
    listen("shop-changed", () => quietSync())
      .then((un) => {
        if (gone) un();
        else stop = un;
      })
      .catch(() => {
        /* Plain vite dev. */
      });
    return () => {
      gone = true;
      stop?.();
    };
  }, [quietSync]);

  // Window title and job tag follow the open work order, else the typed model and serial.
  useEffect(() => {
    jobTitle(current, header.model, header.serial)
      .then(setJobTag)
      .catch(() => {});
  }, [current, header.model, header.serial]);

  // Persist the typed job after a short pause (not while startup is still filling it).
  useEffect(() => {
    if (!loaded.current) return;
    const t = setTimeout(() => {
      jobSetHeader(header.customer, header.model, header.serial).catch(() => {});
    }, 400);
    return () => clearTimeout(t);
  }, [header]);

  /** WorkOrderControl.PickFromList + ShellForm.ApplyWo: open it, remember it, fill the strip. */
  function selectWo(wo: WorkOrder | null) {
    setCurrent(wo);
    if (wo && wo.key) jobSetWo(wo.key).catch(() => {});
    if (wo) {
      setHeader((h) => ({
        customer: wo.customer || h.customer,
        model: wo.model || h.model,
        serial: wo.serial || h.serial,
      }));
    }
  }

  /** Strip edits go into the open work order too (WorkOrderControl.ApplyHeader). */
  function editHeader(h: Header) {
    setHeader(h);
    setCurrent((c) =>
      c ? { ...c, model: h.model, serial: h.serial, customer: h.customer.trim() ? h.customer : c.customer } : c,
    );
  }

  /** Form edits to customer / model / serial show in the strip as well. */
  function editWo(wo: WorkOrder) {
    setCurrent(wo);
    setHeader((h) => ({
      customer: wo.customer.trim() ? wo.customer : h.customer,
      model: wo.model,
      serial: wo.serial,
    }));
  }

  /** Inline7Control.AfterReport: the report goes onto the open work order. */
  async function attachReport(text: string): Promise<string> {
    if (!current) throw new Error("Open or pick a work order first (job strip or Work orders page).");
    const wo = await woAttachReport(current, text);
    setCurrent(wo);
    await reload(wo.key);
    return `Diagnostic report attached to WO ${wo.key}`;
  }

  async function clearJob() {
    await jobClear();
    setCurrent(null);
    setHeader((h) => ({ ...h, customer: "" }));
  }

  const clearPrefill = useCallback(() => setPrefill({ text: "", tick: 0 }), []);

  /** After Add code / note / file: the index is reloaded; show the new entry in Search. */
  function showAdded(query: string) {
    quietSync();
    setAdding(null);
    setAddError("");
    setKbTick((t) => t + 1);
    setPrefill((p) => ({ text: query, tick: p.tick + 1 }));
    setPage("Search");
  }

  async function addFile() {
    try {
      const name = await kbAddFile();
      if (name) showAdded(name);
    } catch (e) {
      setAddError(String(e));
    }
  }

  function searchJob() {
    // JobStrip.SearchQuery: the typed model and serial.
    setPrefill((p) => ({ text: `${header.model} ${header.serial}`.trim(), tick: p.tick + 1 }));
    setPage("Search");
  }

  // Shortcuts from the cheat sheet: F1 help, Ctrl+F / F3 jump to the search box, Esc closes help.
  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key === "F1") {
        e.preventDefault();
        setHelp(true);
      } else if (e.key === "Escape") {
        setHelp(false);
      } else if (e.key === "F3" || ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "f")) {
        e.preventDefault();
        setHelp(false);
        setPage("Search");
        setFocusTick((t) => t + 1);
      }
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  return (
    <div className="shell">
      <nav className="nav" aria-label="Pages">
        <div className="brand">Tech Bench</div>
        {PAGES.map((p) => (
          <button
            key={p}
            className={p === page ? "nav-item active" : "nav-item"}
            aria-current={p === page ? "page" : undefined}
            onClick={() => setPage(p)}
          >
            {p}
          </button>
        ))}
        <button className="nav-item nav-help" onClick={() => setHelp(true)}>
          Cheat sheet (F1)
        </button>
      </nav>
      <div className="main">
        <JobStrip
          data={data}
          current={current}
          header={header}
          onHeader={editHeader}
          onPick={selectWo}
          sync={sync?.chip ? { chip: sync.chip, conflict: sync.conflicts.length > 0 } : null}
          onSync={() => setSyncOpen(true)}
          onSearchJob={searchJob}
          onAddCode={() => setAdding("code")}
          onAddNote={() => setAdding("note")}
          onAddFile={() => void addFile()}
        />
        {addError && (
          <div className="notice add-error" role="alert">
            {addError} <button onClick={() => setAddError("")}>Dismiss</button>
          </div>
        )}
        <main className="page">
          {page === "Search" && (
            <Search
              key={kbTick}
              focusTick={focusTick}
              prefill={prefill}
              onSelected={setHit}
              onPrefilled={clearPrefill}
            />
          )}
          {page === "INLINE 7" && (
            <Inline7 jobTag={jobTag} syncTick={syncCount} onSessionSaved={quietSync} onAttachReport={attachReport} />
          )}
          {page === "Work orders" && (
            <WorkOrders
              data={data}
              current={current}
              onSelect={selectWo}
              onEdit={editWo}
              reload={reload}
              onClearJob={clearJob}
              onShopSettings={() => setPage("Settings")}
            />
          )}
          {page === "Settings" && <ShopSettings onSaved={() => void reload(current?.key)} />}
        </main>
      </div>
      {help && <CheatSheet onClose={() => setHelp(false)} />}
      {adding === "code" && (
        <AddCode
          seedKind={hit?.kind ?? ""}
          seedTitle={hit?.title ?? ""}
          onClose={() => setAdding(null)}
          onSaved={showAdded}
        />
      )}
      {adding === "note" && <AddNote onClose={() => setAdding(null)} onSaved={showAdded} />}
      {syncOpen && (
        <ShopSync
          onSynced={synced}
          onClose={() => {
            // SyncForm closed: the KB may have new shop entries and work orders.
            setSyncOpen(false);
            setKbTick((t) => t + 1);
            void reload(current?.key);
          }}
        />
      )}
    </div>
  );
}
