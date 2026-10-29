import { useEffect, useState } from "react";
import { kbAddCode, kbAddNote, kbCodeSeed, type CodeForm } from "./api";

type CodeField = keyof CodeForm;

const CODE_FIELDS: [CodeField, string, "line" | "text"][] = [
  ["brand", "Brand", "line"],
  ["code", "Code", "line"],
  ["title", "Title", "line"],
  ["severity", "Severity", "line"],
  ["controller", "Controller", "line"],
  ["description", "Description", "text"],
  ["causes", "Likely causes (one per line)", "text"],
  ["checks", "Checks (one per line)", "text"],
  ["reset", "Reset", "text"],
  ["safety", "Safety", "text"],
];

export function Dialog({
  title,
  onClose,
  children,
  footer,
}: {
  title: string;
  onClose: () => void;
  children: React.ReactNode;
  footer: React.ReactNode;
}) {
  return (
    <div
      className="overlay"
      onClick={onClose}
      onKeyDown={(e) => {
        if (e.key === "Escape") {
          e.stopPropagation();
          onClose();
        }
      }}
    >
      <div className="sheet" role="dialog" aria-modal="true" aria-label={title} onClick={(e) => e.stopPropagation()}>
        <div className="sheet-body">
          <h2>{title}</h2>
          {children}
        </div>
        <div className="sheet-foot">{footer}</div>
      </div>
    </div>
  );
}

/** Port of CodeEditForm.cs, seeded from the highlighted search hit (ShopAdd.CodeSeed). */
export function AddCode({
  seedKind,
  seedTitle,
  onClose,
  onSaved,
}: {
  seedKind: string;
  seedTitle: string;
  onClose: () => void;
  onSaved: (query: string) => void;
}) {
  const [form, setForm] = useState<CodeForm | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    kbCodeSeed(seedKind, seedTitle)
      .then(setForm)
      .catch((e) => setError(String(e)));
  }, [seedKind, seedTitle]);

  async function save() {
    if (!form) return;
    try {
      onSaved(await kbAddCode(form));
    } catch (e) {
      setError(String(e));
    }
  }

  return (
    <Dialog
      title="Knowledge base — add / edit a code"
      onClose={onClose}
      footer={
        <>
          <span className="error">{error}</span>
          <button onClick={onClose}>Cancel</button>
          <button className="primary" disabled={!form} onClick={() => void save()}>
            Save to knowledge base
          </button>
        </>
      }
    >
      {form ? (
        <div className="kb-form">
          {CODE_FIELDS.map(([f, label, kind], i) => (
            <label key={f} className={kind === "text" ? "wide" : undefined}>
              {label}
              {kind === "line" ? (
                <input
                  autoFocus={i === 0}
                  value={form[f]}
                  onChange={(e) => setForm({ ...form, [f]: e.target.value })}
                />
              ) : (
                <textarea rows={3} value={form[f]} onChange={(e) => setForm({ ...form, [f]: e.target.value })} />
              )}
            </label>
          ))}
        </div>
      ) : (
        <p className="muted">Loading…</p>
      )}
    </Dialog>
  );
}

/** Port of NoteEditForm.cs. */
export function AddNote({ onClose, onSaved }: { onClose: () => void; onSaved: (query: string) => void }) {
  const [title, setTitle] = useState("");
  const [body, setBody] = useState("");
  const [error, setError] = useState("");

  async function save() {
    try {
      onSaved(await kbAddNote(title, body));
    } catch (e) {
      setError(String(e));
    }
  }

  return (
    <Dialog
      title="Add a tech note"
      onClose={onClose}
      footer={
        <>
          <span className="error">{error}</span>
          <button onClick={onClose}>Cancel</button>
          <button className="primary" onClick={() => void save()}>
            Save note
          </button>
        </>
      }
    >
      <div className="kb-form">
        <label className="wide">
          Title
          <input autoFocus value={title} onChange={(e) => setTitle(e.target.value)} />
        </label>
        <label className="wide">
          Note
          <textarea rows={10} value={body} onChange={(e) => setBody(e.target.value)} />
        </label>
      </div>
    </Dialog>
  );
}
