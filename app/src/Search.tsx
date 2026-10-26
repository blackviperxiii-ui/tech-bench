import { useEffect, useRef, useState } from "react";
import { KINDS, kbInfo, kbSearch, type Hit, type KbInfo } from "./api";

/** Port of SearchControl.cs: query + kind, hit list, detail pane, status line. */
export function Search({ focusTick = 0 }: { focusTick?: number }) {
  const [query, setQuery] = useState("");
  const [kind, setKind] = useState<string>("ALL");
  const [hits, setHits] = useState<Hit[]>([]);
  const [sel, setSel] = useState(0);
  const [status, setStatus] = useState("");
  const [info, setInfo] = useState<KbInfo | null>(null);
  const [error, setError] = useState("");
  const listRef = useRef<HTMLUListElement>(null);
  const queryRef = useRef<HTMLInputElement>(null);

  // Ctrl+F / F3 from anywhere: focus and select the query.
  useEffect(() => {
    if (focusTick > 0) queryRef.current?.select();
  }, [focusTick]);

  useEffect(() => {
    kbInfo()
      .then((i) => {
        setInfo(i);
        setStatus(i.status);
      })
      .catch((e) => setError(String(e)));
  }, []);

  // Search as you type, after the same 220 ms pause as the WinForms debounce.
  useEffect(() => {
    const t = setTimeout(() => {
      kbSearch(query, kind)
        .then((r) => {
          setHits(r.hits);
          setSel(0);
          const count =
            r.total > r.hits.length
              ? `${r.total} hits (showing best ${r.hits.length})`
              : `${r.total} hits`;
          setStatus(`${count}  ·  ${r.status}`);
          setError("");
        })
        .catch((e) => setError(String(e)));
    }, 220);
    return () => clearTimeout(t);
  }, [query, kind]);

  const current = hits[sel];

  function onQueryKey(e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key === "ArrowDown" && hits.length > 0) {
      e.preventDefault();
      listRef.current?.focus();
    }
  }

  function onListKey(e: React.KeyboardEvent<HTMLUListElement>) {
    if (e.key === "ArrowDown") {
      e.preventDefault();
      setSel((s) => Math.min(s + 1, hits.length - 1));
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setSel((s) => Math.max(s - 1, 0));
    }
  }

  let detail = "";
  if (current) {
    detail = `${current.title}\n${current.subtitle}\n\n${current.body}`;
    if (["MANUAL", "FILE", "NOTE"].includes(current.kind)) {
      detail += current.path
        ? `\n\nFile: ${current.path}`
        : "\n\n(No file path recorded for this document.)";
    }
  } else if (info && info.count === 0) {
    detail = `The knowledge base is empty.\n\nExpected data/kb.json under:\n${info.root}`;
  } else if (info) {
    detail = "No hits.";
  }

  return (
    <section className="search">
      <div className="search-bar">
        <input
          ref={queryRef}
          className="query"
          type="search"
          placeholder="Code, SPN, model, serial, filter…"
          aria-label="Search the knowledge base"
          autoFocus
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          onKeyDown={onQueryKey}
        />
        <select aria-label="Kind" value={kind} onChange={(e) => setKind(e.target.value)}>
          {KINDS.map((k) => (
            <option key={k}>{k}</option>
          ))}
        </select>
      </div>
      <div className="status" role="status">
        {error ? `Error: ${error}` : status}
      </div>
      <div className="split">
        <ul
          className="hits"
          ref={listRef}
          tabIndex={0}
          role="listbox"
          aria-label="Results"
          onKeyDown={onListKey}
        >
          {hits.map((h, i) => (
            <li
              key={`${h.kind}:${h.title}:${i}`}
              role="option"
              aria-selected={i === sel}
              className={i === sel ? "hit sel" : "hit"}
              onClick={() => setSel(i)}
            >
              <span className="kind">{h.kind}</span>
              <span className="title">{h.title}</span>
            </li>
          ))}
        </ul>
        <pre className="detail">{detail}</pre>
      </div>
    </section>
  );
}
