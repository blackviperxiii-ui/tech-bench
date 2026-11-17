import { useEffect, useRef, useState } from "react";
import type { LiveDtc } from "../api";

/** Shared pieces of the INLINE 7 live page: the confirm box, a code list and the report preview. */

export interface Ask {
  title: string;
  text: string;
  /** Leave out for an OK-only notice. */
  onYes?: () => void;
}

/** Yes/No box with No focused, so Enter or Esc never confirms by accident. */
export function Confirm({ ask, close }: { ask: Ask; close: () => void }) {
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

export function CodeList({ label, codes, onPick }: { label: string; codes: LiveDtc[]; onPick: (d: LiveDtc) => void }) {
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
export function ReportSheet({
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
