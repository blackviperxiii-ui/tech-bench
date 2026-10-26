import { useEffect, useState } from "react";
import { explainCode, guidanzFeatures, type SwitchRow } from "./api";

/**
 * INLINE 7 page, offline parts: the CodeBook explainer for any SPN/FMI and the Guidanz feature
 * reference. Live J1939 needs the 32-bit RP1210 adapter, so it stays in the Windows build for now.
 */
export function Inline7() {
  const [spn, setSpn] = useState("");
  const [fmi, setFmi] = useState("");
  const [help, setHelp] = useState("Enter an SPN and FMI to explain a fault code.");
  const [rows, setRows] = useState<SwitchRow[]>([]);
  const [row, setRow] = useState(-1);

  useEffect(() => {
    guidanzFeatures()
      .then(setRows)
      .catch(() => setRows([]));
  }, []);

  function explain(e: React.FormEvent) {
    e.preventDefault();
    const s = Number.parseInt(spn, 10);
    const f = Number.parseInt(fmi, 10);
    if (!Number.isInteger(s) || s < 0 || !Number.isInteger(f) || f < 0 || f > 31) {
      setHelp("SPN must be a whole number and FMI 0 to 31.");
      return;
    }
    explainCode(s, f)
      .then((r) => {
        // Same layout as Inline7Control.ShowCode.
        let text = r.text;
        if (r.kb) text += `\n── From the knowledge base ──\n${r.kb}\n`;
        setHelp(text);
      })
      .catch((err) => setHelp(`Error: ${String(err)}`));
  }

  const sel = rows[row];

  return (
    <section className="inline7">
      <p className="notice">
        Live bus needs the Windows build: the INLINE 7 adapter only has 32-bit Windows RP1210
        drivers. Code lookup and the feature reference below work everywhere.
      </p>

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
