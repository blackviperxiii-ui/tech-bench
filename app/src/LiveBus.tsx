import { useCallback, useEffect, useRef, useState } from "react";
import { busCall, busReport, sessionSave, type BusAdapter, type BusCaps, type BusSnap, type LiveDtc } from "./api";

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

interface Ask {
  title: string;
  text: string;
  /** Leave out for an OK-only notice. */
  onYes?: () => void;
}

/** Yes/No box with No focused, so Enter or Esc never confirms by accident. */
function Confirm({ ask, close }: { ask: Ask; close: () => void }) {
  const no = useRef<HTMLButtonElement>(null);
  useEffect(() => no.current?.focus(), []);
  return (
    <div
      className="overlay"
      role="alertdialog"
      aria-modal="true"
      aria-label={ask.title}
      onKeyDown={(e) => {
        if (e.key === "Escape") close();
      }}
    >
      <div className="sheet confirm">
        <div className="sheet-body">
          <h2>{ask.title}</h2>
          <p className="confirm-text">{ask.text}</p>
        </div>
        <div className="sheet-foot">
          {ask.onYes ? (
            <>
              <button
                type="button"
               
                onClick={() => {
                  close();
                  ask.onYes?.();
                }}
              >
                Yes
              </button>
              <button type="button" className="primary" ref={no} onClick={close}>
                No
              </button>
            </>
          ) : (
            <button type="button" className="primary" ref={no} onClick={close}>
              OK
            </button>
          )}
        </div>
      </div>
    </div>
  );
}

function CodeList({ label, codes, onPick }: { label: string; codes: LiveDtc[]; onPick: (d: LiveDtc) => void }) {
  return (
    <div className="live-codes">
      <h3>{label}</h3>
      <ul className="hits mini" role="listbox" aria-label={label} tabIndex={0}>
        {codes.length === 0 && <li className="empty">(none)</li>}
        {codes.map((d) => (
          <li key={`${d.spn}/${d.fmi}`} role="option" aria-selected={false} className="hit" onClick={() => onPick(d)}>
            <span className="title">{d.text}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}

/** Report preview (JobReportPrint): Print uses the browser print dialog with print CSS. */
function ReportSheet({
  text,
  close,
  attach,
}: {
  text: string;
  close: () => void;
  attach: (text: string) => Promise<string>;
}) {
  const [note, setNote] = useState("");
  return (
    <div className="overlay report-overlay" role="dialog" aria-modal="true" aria-label="Diagnostic report">
      <div className="sheet">
        <div className="sheet-body">
          <pre className="report-text">{text}</pre>
        </div>
        <div className="sheet-foot">
          <span className="muted foot-note" role="status">
            {note}
          </span>
          <button
            type="button"
           
            onClick={() => {
              attach(text)
                .then(setNote)
                .catch((e) => setNote(`Could not attach report: ${e instanceof Error ? e.message : String(e)}`));
            }}
          >
            Attach to work order
          </button>
          <button type="button" onClick={() => window.print()}>
            Print
          </button>
          <button type="button" className="primary" onClick={close}>
            Close
          </button>
        </div>
      </div>
    </div>
  );
}

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
}: {
  caps: BusCaps;
  jobTag: string;
  /** Unit history text for the report's UNIT HISTORY section. */
  history: string;
  onCode: (spn: number, fmi: number) => void;
  onReport: (text: string) => void;
  onSaved: () => void;
  onAttach: (text: string) => Promise<string>;
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
        const s = await busCall<BusSnap>("snapshot");
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

      <h3>Log</h3>
      <pre className="detail mini live-log" aria-label="Bus log">
        {log.join("\n")}
      </pre>

      {report !== null && <ReportSheet text={report} close={() => setReport(null)} attach={onAttach} />}
      {ask && <Confirm ask={ask} close={() => setAsk(null)} />}
    </div>
  );
}
