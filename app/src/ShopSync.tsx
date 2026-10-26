import { useEffect, useState } from "react";
import { Dialog } from "./KbAdd";
import { syncPickFolder, syncResolve, syncRun, syncSave, syncView, type SyncView } from "./api";

/** Port of SyncForm.cs: who this PC is, which folder to share, the last result, and conflicts. */
export function ShopSync({ onClose, onSynced }: { onClose: () => void; onSynced: (v: SyncView) => void }) {
  const [tech, setTech] = useState("");
  const [folder, setFolder] = useState("");
  const [view, setView] = useState<SyncView | null>(null);
  const [sel, setSel] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  function show(v: SyncView) {
    setView(v);
    setTech(v.techId);
    setFolder(v.syncFolder);
    setSel(0);
    setError("");
  }

  useEffect(() => {
    syncView()
      .then(show)
      .catch((e) => setError(String(e)));
  }, []);

  async function act(step: () => Promise<SyncView | null>) {
    setBusy(true);
    try {
      const v = await step();
      if (v) {
        show(v);
        if (v.chip) onSynced(v);
      }
    } catch (e) {
      setError(String(e));
    } finally {
      setBusy(false);
    }
  }

  const save = () => act(() => syncSave(tech, folder));
  const runNow = () =>
    act(async () => {
      await syncSave(tech, folder);
      return syncRun();
    });
  const useKb = () => act(() => syncSave(tech, ""));
  const resolve = (choice: "local" | "remote" | "both") => {
    if (!view?.conflicts.length) {
      setError("Select a conflict first.");
      return;
    }
    void act(() => syncResolve(sel, choice));
  };

  const conflicts = view?.conflicts ?? [];
  return (
    <Dialog
      title="Shop sync"
      onClose={onClose}
      footer={
        <>
          <span className="error">{error}</span>
          <button disabled={busy || !conflicts.length} onClick={() => resolve("local")}>
            Keep mine
          </button>
          <button disabled={busy || !conflicts.length} onClick={() => resolve("remote")}>
            Keep theirs
          </button>
          <button disabled={busy || !conflicts.length} onClick={() => resolve("both")}>
            Keep both
          </button>
          <button className="primary" onClick={onClose}>
            Close
          </button>
        </>
      }
    >
      <div className="kb-form sync-form">
        <label className="wide">
          Tech name
          <input autoFocus value={tech} onChange={(e) => setTech(e.target.value)} />
        </label>
        <label className="wide">
          Sync folder
          <span className="row">
            <input value={folder} onChange={(e) => setFolder(e.target.value)} />
            <button
              disabled={busy}
              onClick={() =>
                void syncPickFolder()
                  .then((p) => p && setFolder(p))
                  .catch((e) => setError(String(e)))
              }
            >
              Browse…
            </button>
          </span>
        </label>
      </div>
      <p className="muted">
        Leave the folder blank if this knowledge base is already the shared tree (OneDrive). Otherwise pick a USB
        stick, network share, or a folder both techs copy to. Conflicts are listed below — nothing overwrites a note
        without you picking.
      </p>
      <div className="bar">
        <button disabled={busy} onClick={() => void save()}>
          Save settings
        </button>
        <button disabled={busy} onClick={() => void runNow()}>
          Sync now
        </button>
        <button disabled={busy} onClick={() => void useKb()}>
          Use KB folder
        </button>
      </div>
      <pre className="detail sync-status" role="status">
        {view?.text ?? "Loading…"}
      </pre>
      <h3>Conflicts</h3>
      <select
        className="sync-conflicts"
        size={6}
        aria-label="Conflicts"
        value={conflicts.length ? sel : ""}
        onChange={(e) => setSel(Number(e.target.value))}
      >
        {conflicts.map((c, i) => (
          <option key={i} value={i}>
            {c}
          </option>
        ))}
      </select>
    </Dialog>
  );
}
