import { useCallback, useEffect, useRef, useState } from "react";
import { busCall, busReport, sessionSave, type BusAdapter, type BusCaps, type BusSnap } from "./api";
import { BusTab } from "./live/BusTab";
import { TimelineTab } from "./live/TimelineTab";
import { TrendTab } from "./live/TrendTab";
import { CodeList, Confirm, ReportSheet, type Ask } from "./live/parts";

/** Inner tabs, in the WinForms INLINE 7 order; the rest arrive with their own pages. */
const TABS = ["Codes", "Bus", "Trend", "Timeline"] as const;
type Tab = (typeof TABS)[number];

/** How often the HUD asks the bridge for a snapshot (the WinForms UI timer is 250 ms). */
const POLL_MS = 400;
const MAX_LOG_LINES = 400;

/** Same wording and Yes/No as Inline7Control.DoResetAll and DoClearPrevious; No is the default. */
const RESET_ALL =
  "Reset all codes on the engine, the compressor controller (SA 48), and any DEF/aftertreatment module reporting faults?\n\n" +
  "Sends J1939 DM11 (clear active) then DM3 (clear previously active) to each module directly and to everyone, waits for each one to ACK or refuse, then reads DM1/DM2 and the DEF/SCR tank message back. Takes a few seconds.\n\n" +
  "Red Stop and Amber are not separate codes. They are lamp bits on DM1. They go OFF by themselves when the DTCs that set them are gone.\n" +
  "There is no legal 'Red Stop off' / 'Amber off' switch — forcing the lamps off while 5246 / FMI 9 are still active would hide a no-fuel command, not diagnose it.\n\n" +
  "Many ECMs only accept a clear with key ON, engine OFF. If a fault is still true it comes straight back; the report says which.\n\nContinue?";
const CLEAR_PREVIOUS =
  "Clear PREVIOUSLY ACTIVE codes only (DM3) on the engine, the compressor controller (SA 48), and any other module reporting faults?\n\n" +
  "Each module is asked directly and has to ACK. Active faults that are still happening will stay.";
const SAFETY_FIRST =
  "Turn ON the safety toggle first. Speed request is only for an engine that is already running, unloaded.\n" +
  "It will not start a shut-off engine and it will not override Red Stop or DEF inducement.";

/**
 * Live J1939 half of the INLINE 7 page (Inline7Control's toolbar, HUD and code lists), over the
 * RP1210 bridge. Every send is a named bridge command that the bridge checks against the guard.
 */
export function LiveBus({
  caps,
  jobTag,
  history,
  onCode,
  onReport,
  onSaved,
  onAttach,
  onSearch,
}: {
  caps: BusCaps;
  jobTag: string;
  /** Unit history text for the report's UNIT HISTORY section. */
  history: string;
  onCode: (spn: number, fmi: number) => void;
  onReport: (text: string) => void;
  onSaved: () => void;
  onAttach: (text: string) => Promise<string>;
  onSearch: (query: string) => void;
}) {
  const [report, setReport] = useState<string | null>(null);
  const [adapters, setAdapters] = useState<BusAdapter[]>([]);
  const [adapter, setAdapter] = useState("");
  const [snap, setSnap] = useState<BusSnap | null>(null);
  const [log, setLog] = useState<string[]>([]);
  const [safe, setSafe] = useState(false);
  const [hold, setHold] = useState(0);
  const [quiet, setQuiet] = useState(false);
  const [clearing, setClearing] = useState("");
  const [ask, setAsk] = useState<Ask | null>(null);
  const [tab, setTab] = useState<Tab>("Codes");
  // The bridge starts with auto-reconnect on, as the WinForms checkbox does.
  const [autoRe, setAutoRe] = useState(true);
  // Module picker: -1 is "Engine (auto)". The poll sends it with every snapshot.
  const [view, setView] = useState(-1);
  const viewRef = useRef(-1);
  const shownReset = useRef(0);
  const pollBusy = useRef(false);

  const addLog = useCallback((lines: string[]) => {
    if (lines.length === 0) return;
    setLog((l) => [...l, ...lines].slice(-MAX_LOG_LINES));
  }, []);

  const run = useCallback(
    async <T,>(cmd: string, args?: Record<string, unknown>) => {
      try {
        return await busCall<T>(cmd, args);
      } catch (e) {
        addLog([`${cmd}: ${String(e)}`]);
        return null;
      }
    },
    [addLog],
  );

  useEffect(() => {
    void run<BusAdapter[]>("adapters").then((list) => {
      if (!list) return;
      setAdapters(list);
      const pick = list.find((a) => a.inline7) ?? list[0];
      if (pick) setAdapter(pick.id);
    });
  }, [run]);

  // The UI timer: snapshot and the worker log. A code clear holds the bridge, so polls queue
  // behind it rather than overlap.
  useEffect(() => {
    let gone = false;
    const tick = async () => {
      if (pollBusy.current) return;
      pollBusy.current = true;
      try {
        const s = await busCall<BusSnap>("snapshot", { viewSa: viewRef.current });
        if (gone) return;
        setSnap(s);
        addLog(await busCall<string[]>("log"));
        if (s.resetSeq !== shownReset.current && s.lastResetReport) {
          shownReset.current = s.resetSeq;
          onReport(s.lastResetReport);
        }
      } catch {
        /* The next tick tries again; a stopped bridge restarts on the next call. */
      } finally {
        pollBusy.current = false;
      }
    };
    const t = window.setInterval(() => void tick(), POLL_MS);
    void tick();
    return () => {
      gone = true;
      window.clearInterval(t);
    };
  }, [addLog, onReport]);

  const connected = snap?.connected ?? false;
  const busy = (snap?.busy ?? false) || clearing !== "";
  const idle = connected && !busy;

  function connect() {
    if (!adapter) {
      setAsk({
        title: "Connect",
        text: "No RP1210 adapter is installed on this PC.\n\nInstall the Cummins INLINE 7 (CIL7) drivers, then restart Tech Bench.",
      });
      return;
    }
    addLog([`Connecting to ${adapter} … close Guidanz / J1939 tool / USB-Link Explorer first.`]);
    void run("connect", { adapter });
  }

  async function disconnect() {
    // DoDisconnect: release TSC1, and leave modules broadcasting (quiet bus is session-scoped).
    setHold(0);
    await run("tsc1", { rpm: 0 });
    if (quiet) {
      setQuiet(false);
      await run("dm13", { stop: false });
    }
    await run("disconnect");
  }

  async function clear(kind: "all" | "previous") {
    const what = kind === "all" ? "Reset all codes" : "Clear previous";
    setClearing(what);
    addLog([`${what} running — the bus answers again when it finishes.`]);
    const report = await run<string>("clear", { kind });
    setClearing("");
    if (report) onReport(report);
  }

  async function setSafety(on: boolean) {
    setSafe(on);
    await run("safety", { allowHold: on });
    if (!on) setHold(0);
  }

  async function setTsc(rpm: number) {
    if (rpm > 0 && !safe) {
      setAsk({ title: "Speed request", text: SAFETY_FIRST });
      return;
    }
    const held = await run<number>("tsc1", { rpm });
    setHold(held ?? 0);
  }

  async function setQuietBus(on: boolean) {
    setQuiet(on);
    await run("dm13", { stop: on });
  }

  async function save() {
    try {
      const r = await sessionSave(jobTag);
      addLog([`Saved ${r.path}`]);
      onSaved();
    } catch (e) {
      setAsk({ title: "Save", text: `Could not write the session file.\n\n${String(e)}` });
    }
  }

  async function openReport() {
    try {
      // History.Summary only means something with a job tag (BuildReportData).
      setReport(await busReport(jobTag, jobTag.trim() ? history : ""));
    } catch (e) {
      setAsk({ title: "Report", text: `Could not build the report.\n\n${String(e)}` });
    }
  }

  function pickModule(sa: number) {
    viewRef.current = sa;
    setView(sa);
  }

  // RefreshModuleList: Engine (auto), then each module with faults; a module that left falls
  // back to Engine (auto).
  const modules = (snap?.faultModules ?? []).map((sa, i) => ({ sa, label: snap?.faultModuleLabels[i] ?? `SA ${sa}` }));
  useEffect(() => {
    if (view !== -1 && snap && !snap.faultModules.includes(view)) pickModule(-1);
  }, [snap, view]);

  const hud = snap?.hud;
  const status = clearing ? `Working: ${clearing}…` : (hud?.status ?? "Starting the adapter bridge…");

  return (
    <div className="live">
      {caps.fake && <p className="notice">Fake adapter: scripted ECMs for tests, no hardware.</p>}
      <div className="bar">
        <select aria-label="Adapter" value={adapter} disabled={connected || busy} onChange={(e) => setAdapter(e.target.value)}>
          {adapters.length === 0 && <option value="">(no RP1210 adapter installed)</option>}
          {adapters.map((a) => (
            <option key={a.id} value={a.id}>
              {a.name}
            </option>
          ))}
        </select>
        <label className="check">
          Module
          <select
            aria-label="Module"
            title="Whose DM1/DM2 the code lists show. On a portable compressor the controller at SA 48 has its own faults."
            value={view}
            onChange={(e) => pickModule(Number(e.target.value))}
          >
            <option value={-1}>Engine (auto)</option>
            {modules.map((m) => (
              <option key={m.sa} value={m.sa}>
                {m.label}
              </option>
            ))}
          </select>
        </label>
        <button type="button" disabled={connected || busy} onClick={connect}>
          Connect
        </button>
        <button type="button" disabled={!connected && !busy} onClick={() => void disconnect()}>
          Disconnect
        </button>
        <button type="button" disabled={!idle} onClick={() => void run("requestCodes")}>
          Refresh codes
        </button>
        <button type="button" disabled={!idle} onClick={() => void run("pingIdentity")}>
          Ping ECM
        </button>
        <button
          type="button"
          disabled={!idle}
          onClick={() => setAsk({ title: "Reset all codes", text: RESET_ALL, onYes: () => void clear("all") })}
        >
          Reset all codes
        </button>
        <button
          type="button"
          disabled={!idle}
          onClick={() => setAsk({ title: "Clear previous", text: CLEAR_PREVIOUS, onYes: () => void clear("previous") })}
        >
          Clear previous
        </button>
        <button type="button" disabled={busy} onClick={() => void save()}>
          Save
        </button>
        <button type="button" disabled={busy} onClick={() => void openReport()}>
          Report
        </button>
      </div>

      <p className="status" role="status">
        {status}
      </p>
      <div className={snap?.red ? "hud red" : "hud"}>
        <span className="hud-rpm">{hud?.rpm ?? "RPM: —"}</span>
        <span>{hud?.lamps ?? "Lamps: —"}</span>
        <span>{hud?.def ?? "DEF: —"}</span>
      </div>
      <p className="hud-mon">{hud?.mon ?? ""}</p>
      {snap?.aftText && <p className="hud-mon">{snap.aftText}</p>}

      <div className="tabs" role="tablist" aria-label="INLINE 7 tabs">
        {TABS.map((t) => (
          <button
            key={t}
            type="button"
            role="tab"
            aria-selected={tab === t}
            className={tab === t ? "tab on" : "tab"}
            onClick={() => setTab(t)}
          >
            {t}
          </button>
        ))}
      </div>

      {tab === "Codes" && (
        <div role="tabpanel" aria-label="Codes">
        <div className="live-grid">
          <CodeList label="Active codes (DM1)" codes={snap?.active ?? []} onPick={(d) => onCode(d.spn, d.fmi)} />
          <CodeList label="Previously active (DM2)" codes={snap?.prev ?? []} onPick={(d) => onCode(d.spn, d.fmi)} />
        </div>

        <fieldset className="toggles">
          <legend>Diagnostic toggles</legend>
          <label title="Must be ON before a speed-request toggle will send TSC1. Turning this OFF releases any active TSC1 hold. The engine will not start from these switches. TSC1 cannot override Red Stop or DEF inducement.">
            <input type="checkbox" checked={safe} onChange={(e) => void setSafety(e.target.checked)} />
            Safety: engine already running, compressor unloaded, area clear
          </label>
          <label title="ON: send J1939 TSC1 speed-control 800 RPM every 40 ms while the engine is already running. OFF: release control back to the ECM. Requires the safety toggle.">
            <input type="checkbox" disabled={!connected} checked={hold === 800} onChange={(e) => void setTsc(e.target.checked ? 800 : 0)} />
            Hold idle 800 RPM (TSC1)
          </label>
          <label title="ON: send J1939 TSC1 speed-control 1200 RPM every 40 ms while the engine is already running. OFF: release control back to the ECM. Requires the safety toggle.">
            <input type="checkbox" disabled={!connected} checked={hold === 1200} onChange={(e) => void setTsc(e.target.checked ? 1200 : 0)} />
            Hold 1200 RPM (TSC1)
          </label>
          <label title="ON: J1939-73 DM13 Stop Broadcast, so you can see what still talks. OFF: DM13 Start Broadcast. Do not leave ON. This is not a sensor disable and not a DEF bypass.">
            <input type="checkbox" disabled={!connected} checked={quiet} onChange={(e) => void setQuietBus(e.target.checked)} />
            Quiet bus (DM13 stop J1939 broadcast)
          </label>
        </fieldset>
        </div>
      )}
      {tab === "Bus" && (
        <div role="tabpanel" aria-label="Bus">
          <BusTab
            jobTag={jobTag}
            run={run}
            addLog={addLog}
            autoReconnect={autoRe}
            onAutoReconnect={(on) => {
              setAutoRe(on);
              void run("autoReconnect", { on });
            }}
          />
        </div>
      )}
      {tab === "Trend" && (
        <div role="tabpanel" aria-label="Trend">
          <TrendTab jobTag={jobTag} run={run} addLog={addLog} />
        </div>
      )}
      {tab === "Timeline" && (
        <div role="tabpanel" aria-label="Timeline">
          <TimelineTab jobTag={jobTag} addLog={addLog} onSearch={onSearch} />
        </div>
      )}

      <h3>Log</h3>
      <pre className="detail mini live-log" aria-label="Bus log">
        {log.join("\n")}
      </pre>

      {report !== null && <ReportSheet text={report} close={() => setReport(null)} attach={onAttach} />}
      {ask && <Confirm ask={ask} close={() => setAsk(null)} />}
    </div>
  );
}
