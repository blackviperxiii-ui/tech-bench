import { useEffect, useState } from "react";
import { CheatSheet } from "./CheatSheet";
import { Inline7 } from "./Inline7";
import { Search } from "./Search";
import { runSmoke } from "./smoke";
import { smokeMode } from "./api";

/** Same main pages as ShellForm; screens not ported yet say so. */
const PAGES = ["Search", "Work orders", "INLINE 7", "Settings"] as const;
type Page = (typeof PAGES)[number];

export function App() {
  const [page, setPage] = useState<Page>("Search");
  const [help, setHelp] = useState(false);
  const [focusTick, setFocusTick] = useState(0);

  useEffect(() => {
    smokeMode()
      .then((on) => {
        if (on) void runSmoke();
      })
      .catch(() => {
        /* Not running inside Tauri (plain vite dev): no smoke mode. */
      });
  }, []);

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
      <main className="page">
        {page === "Search" && <Search focusTick={focusTick} />}
        {page === "INLINE 7" && <Inline7 />}
        {(page === "Work orders" || page === "Settings") && (
          <div className="todo">
            <h2>{page}</h2>
            <p>Not ported yet. Use the Windows Tech Bench app for this screen for now.</p>
          </div>
        )}
      </main>
      {help && <CheatSheet onClose={() => setHelp(false)} />}
    </div>
  );
}
