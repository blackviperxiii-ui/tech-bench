import { useEffect, useState } from "react";
import { Search } from "./Search";
import { runSmoke } from "./smoke";
import { smokeMode } from "./api";

const PAGES = [
  "Search",
  "Code book",
  "Feature book",
  "Notes",
  "Work orders",
  "Job report",
  "Cheat sheet",
  "Settings",
] as const;
type Page = (typeof PAGES)[number];

export function App() {
  const [page, setPage] = useState<Page>("Search");

  useEffect(() => {
    smokeMode()
      .then((on) => {
        if (on) void runSmoke();
      })
      .catch(() => {
        /* Not running inside Tauri (plain vite dev): no smoke mode. */
      });
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
      </nav>
      <main className="page">
        {page === "Search" ? (
          <Search />
        ) : (
          <div className="todo">
            <h2>{page}</h2>
            <p>Not ported yet. Use the Windows Tech Bench app for this screen for now.</p>
          </div>
        )}
      </main>
    </div>
  );
}
