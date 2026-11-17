import { useEffect, useState } from "react";
import { busCall, busExport } from "../api";

interface Row {
  spn: number;
  fmi: number;
  /** DtcEvent text, as the WinForms list shows it. */
  text: string;
  /** What a double-click searches the KB for (Inline7Control.KbQuery). */
  query: string;
}

const TIMELINE_MS = 1000;

/**
 * Timeline tab (Inline7Control.BuildTimelineTab): every code that came and went on this hookup for
 * the module in view, newest first. Double-click or Enter searches the KB for the code.
 */
export function TimelineTab({
  jobTag,
  addLog,
  onSearch,
}: {
  jobTag: string;
  addLog: (lines: string[]) => void;
  onSearch: (query: string) => void;
}) {
  const [rows, setRows] = useState<Row[]>([]);
  const [sel, setSel] = useState(-1);

  useEffect(() => {
    let gone = false;
    const tick = () =>
      // No `sa`: the bridge uses the module picked in the Module list.
      void busCall<{ rows: Row[] }>("timeline", { job: jobTag })
        .then((t) => {
          if (!gone) setRows(t.rows);
        })
        .catch(() => {
          /* The next tick tries again. */
        });
    const t = window.setInterval(tick, TIMELINE_MS);
    tick();
    return () => {
      gone = true;
      window.clearInterval(t);
    };
  }, [jobTag]);

  async function exportCsv() {
    try {
      addLog([`Timeline exported ${await busExport("timeline", jobTag)}`]);
    } catch (e) {
      addLog([`Timeline export failed: ${String(e)}`]);
    }
  }

  return (
    <div className="timeline-tab">
      <div className="bar">
        <span className="bar-note">Every code that came and went on this hookup, newest first.</span>
        <button type="button" onClick={() => void exportCsv()}>
          Export CSV
        </button>
      </div>
      <ul
        className="hits mini timeline-list"
        role="listbox"
        aria-label="Timeline"
        tabIndex={0}
        onKeyDown={(e) => {
          if (e.key === "Enter" && rows[sel]) onSearch(rows[sel].query);
          else if (e.key === "ArrowDown") setSel((i) => Math.min(rows.length - 1, i + 1));
          else if (e.key === "ArrowUp") setSel((i) => Math.max(0, i - 1));
        }}
      >
        {rows.length === 0 && <li className="empty">(nothing yet — connect and let DM1 arrive)</li>}
        {rows.map((r, i) => (
          <li
            key={`${r.spn}/${r.fmi}`}
            role="option"
            aria-selected={i === sel}
            className={i === sel ? "hit sel" : "hit"}
            title="Double-click to search the KB for this code"
            onClick={() => setSel(i)}
            onDoubleClick={() => onSearch(r.query)}
          >
            <span className="title">{r.text}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}
