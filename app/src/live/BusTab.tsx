import { useEffect, useState } from "react";
import { busCall, busExport, localNow, type BusLists } from "../api";

/** How often the open Bus tab re-reads its lists (the WinForms page refreshed them about once a second). */
const LIST_MS = 1000;

/** First text in the Diff box, as the WinForms Bus tab shows it. */
export const DIFF_HELP =
  "Snapshot A before a repair, Snapshot B after, then Diff.\r\n" +
  "PGN list updates live. SA 0 = ECM, 48 = compressor controller, 128 = Grayhill. " +
  "If the tank header talks you will see DEF PGN FE56 from the engine.";

/**
 * Bus tab (Inline7Control.BuildBusTab): the auto-reconnect toggle, who is talking on the bus,
 * Snapshot A / B with a Diff, Clear stats, and Export CSV of the two lists.
 */
export function BusTab({
  jobTag,
  run,
  addLog,
  autoReconnect,
  onAutoReconnect,
}: {
  jobTag: string;
  run: <T>(cmd: string, args?: Record<string, unknown>) => Promise<T | null>;
  addLog: (lines: string[]) => void;
  /** "Auto-reconnect if the adapter drops": kept by the live page so it survives a tab switch. */
  autoReconnect: boolean;
  onAutoReconnect: (on: boolean) => void;
}) {
  const [lists, setLists] = useState<BusLists>({ pgnLines: [], saLines: [] });
  const [diff, setDiff] = useState(DIFF_HELP);

  useEffect(() => {
    let gone = false;
    const tick = () =>
      void busCall<BusLists>("busLists")
        .then((l) => {
          if (!gone) setLists(l);
        })
        .catch(() => {
          /* The next tick tries again. */
        });
    const t = window.setInterval(tick, LIST_MS);
    tick();
    return () => {
      gone = true;
      window.clearInterval(t);
    };
  }, []);

  async function snap(which: "A" | "B") {
    const d = await run<string>("snap", { which, time: localNow().local.slice(11) });
    if (d !== null) setDiff(d);
  }

  async function showDiff() {
    const d = await run<string>("diff");
    if (d !== null) setDiff(d);
  }

  async function exportCsv() {
    try {
      addLog([`Exported ${await busExport("bus", jobTag)}`]);
    } catch (e) {
      addLog([`Export CSV: ${String(e)}`]);
    }
  }

  return (
    <div className="bus-tab">
      <div className="bar">
        <label className="check">
          <input type="checkbox" checked={autoReconnect} onChange={(e) => onAutoReconnect(e.target.checked)} />
          Auto-reconnect if the adapter drops
        </label>
        <button type="button" onClick={() => void snap("A")}>
          Snapshot A
        </button>
        <button type="button" onClick={() => void snap("B")}>
          Snapshot B
        </button>
        <button type="button" onClick={() => void showDiff()}>
          Diff
        </button>
        <button type="button" onClick={() => void run("clearBusStats")}>
          Clear stats
        </button>
        <button type="button" onClick={() => void exportCsv()}>
          Export CSV
        </button>
      </div>
      <div className="live-grid">
        <div>
          <h3>PGN traffic (who is talking)</h3>
          <pre className="detail mini bus-list" aria-label="PGN traffic">
            {lists.pgnLines.join("\n")}
          </pre>
        </div>
        <div>
          <h3>Address claim / modules</h3>
          <pre className="detail mini bus-list" aria-label="Address claim">
            {lists.saLines.join("\n")}
          </pre>
        </div>
      </div>
      <pre className="detail mini bus-diff" aria-label="Snapshot diff">
        {diff.replace(/\r\n/g, "\n")}
      </pre>
    </div>
  );
}
