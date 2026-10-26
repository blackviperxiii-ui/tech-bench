import { useCallback, useEffect, useState } from "react";
import { busCaps, explainCode, guidanzFeatures, historySummary, type BusCaps, type SwitchRow } from "./api";
import { LiveBus } from "./LiveBus";

/**
 * INLINE 7 page: live J1939 over the RP1210 bridge where this build has one (Windows), then the
 * offline parts that work everywhere: the CodeBook explainer, unit history and the Guidanz
 * feature reference.
 */
export function Inline7({
  jobTag,
  syncTick,
  onSessionSaved,
  onAttachReport,
}: {
  jobTag: string;
  syncTick: number;
  onSessionSaved: () => void;
  onAttachReport: (text: string) => Promise<string>;
}) {
  const [savedTick, setSavedTick] = useState(0);
  const [spn, setSpn] = useState("");
  const [fmi, setFmi] = useState("");
  const [help, setHelp] = useState("Enter an SPN and FMI to explain a fault code.");
  const [rows, setRows] = useState<SwitchRow[]>([]);
  const [row, setRow] = useState(-1);

  const [history, setHistory] = useState({ text: "", folder: "" });
  const [caps, setCaps] = useState<BusCaps | null>(null);

  useEffect(() => {
    busCaps()
      .then(setCaps)
      .catch((e) => setCaps({ available: false, fake: false, reason: String(e) }));
  }, []);

  // Inline7Control.RefreshHistory: follows the job tag, and a sync may bring other techs' visits.
  useEffect(() => {
    historySummary(jobTag)
      .then(setHistory)
      .catch((e) => setHistory({ text: String(e), folder: "" }));
  }, [jobTag, syncTick, savedTick]);

  useEffect(() => {
    guidanzFeatures()
      .then(setRows)
      .catch(() => setRows([]));
  }, []);

  const show = useCallback((s: number, f: number) => {
    explainCode(s, f)
      .then((r) => {
        // Same layout as Inline7Control.ShowCode.
        let text = r.text;
        if (r.kb) text += `\n── From the knowledge base ──\n${r.kb}\n`;
        setHelp(text);
      })
      .catch((err) => setHelp(`Error: ${String(err)}`));
  }, []);

  /** A live code was clicked: fill the form too, so the tech sees which code is explained. */
  const pick = useCallback(
    (s: number, f: number) => {
      setSpn(String(s));
      setFmi(String(f));
      show(s, f);
    },
    [show],
  );

  function explain(e: React.FormEvent) {
    e.preventDefault();
    const s = Number.parseInt(spn, 10);
    const f = Number.parseInt(fmi, 10);
    if (!Number.isInteger(s) || s < 0 || !Number.isInteger(f) || f < 0 || f > 31) {
      setHelp("SPN must be a whole number and FMI 0 to 31.");
      return;
    }
    show(s, f);
  }

  const sel = rows[row];

  return (
    <section className="inline7">
      <h2>Live bus</h2>
      {caps?.available ? (
        <LiveBus
          caps={caps}
          jobTag={jobTag}
          history={history.text}
          onCode={pick}
          onReport={setHelp}
          onSaved={() => {
            setSavedTick((t) => t + 1);
            onSessionSaved();
          }}
          onAttach={onAttachReport}
        />
      ) : (
        <p className="notice">
          {caps?.reason ?? "Checking for the adapter bridge…"} Code lookup, unit history and the
          feature reference below work everywhere.
        </p>
      )}

      <h2>Explain a code</h2>
      <form className="code-form" onSubmit={explain}>
        <label>
          SPN
          <input inputMode="numeric" value={spn} onChange={(e) => setSpn(e.target.value)} />
        </label>
        <label>
          FMI
          <input inputMode="numeric" value={fmi} onChange={(e) => setFmi(e.target.value)} />
        </label>
        <button type="submit">Explain</button>
      </form>
      <pre className="detail code-help">{help}</pre>

      <h2>Unit history</h2>
      <p className="muted">
        Faults from earlier saved sessions for {jobTag ? `"${jobTag}"` : "this job"}, including other techs&apos;
        visits that shop sync brought in.
      </p>
      <pre className="detail history" aria-label="Unit history">
        {history.text}
        {history.folder ? `\nSession folder: ${history.folder}` : ""}
      </pre>

      <h2>Guidanz features</h2>
      <p className="muted">
        Cummins Features &amp; Parameters this app does NOT write. Reference only, so you know what
        lives in Guidanz and what does not belong in a J1939 tool at all.
      </p>
      <div className="split short">
        <ul className="hits" role="listbox" aria-label="Guidanz features" tabIndex={0}>
          {rows.map((r, i) => (
            <li
              key={r.id}
              role="option"
              aria-selected={i === row}
              className={i === row ? "hit sel" : "hit"}
              onClick={() => setRow(i)}
            >
              <span className="kind">{r.state}</span>
              <span className="title">
                {r.name} — {r.short}
              </span>
            </li>
          ))}
        </ul>
        <pre className="detail">{sel ? `${sel.name}\n${sel.state}\n\n${sel.detail}` : "Click an item."}</pre>
      </div>
    </section>
  );
}
