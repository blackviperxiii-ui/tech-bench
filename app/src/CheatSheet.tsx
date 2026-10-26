import { useEffect, useRef, useState } from "react";
import { cheatsheet, type Para } from "./api";

/** Port of CheatSheetForm.cs: the shipped quick reference, opened with F1, closed with Esc. */
export function CheatSheet({ onClose }: { onClose: () => void }) {
  const [paras, setParas] = useState<Para[] | null>(null);
  const [error, setError] = useState("");
  const closeRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    cheatsheet()
      .then(setParas)
      .catch((e) => setError(String(e)));
    closeRef.current?.focus();
  }, []);

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-label="Quick reference">
      <div className="sheet">
        <div className="sheet-body">
          {error && <p>Could not read the cheat sheet: {error}</p>}
          {paras?.map((p, i) => {
            if (!p.text.trim()) return null;
            if (p.big) return <h2 key={i}>{p.text}</h2>;
            if (p.bold) return <h3 key={i}>{p.text}</h3>;
            const tab = p.text.indexOf("\t");
            if (tab < 0) return <p key={i}>{p.text}</p>;
            return (
              <div className="ref-row" key={i}>
                <span className="ref-key">{p.text.slice(0, tab)}</span>
                <span>{p.text.slice(tab + 1).replace(/\t/g, " ")}</span>
              </div>
            );
          })}
        </div>
        <div className="sheet-foot">
          <button ref={closeRef} onClick={onClose}>
            Close
          </button>
        </div>
      </div>
    </div>
  );
}
