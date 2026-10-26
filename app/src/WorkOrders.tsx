import { useState } from "react";
import { woAddPhotos, woClock, woOpenMedia, woSave, woShare, type WorkOrder, type WoList } from "./api";

export const IDLE_STATUS =
  "Work orders are saved on this PC and in the shop folder. Share with shop copies one for other techs.";

export function blankWorkOrder(techId: string): WorkOrder {
  const d = new Date();
  const p = (n: number) => String(n).padStart(2, "0");
  // Same LOCAL-yyyyMMdd-HHmmss placeholder as WorkOrderControl.NewOrder (local time).
  const stamp = `${d.getFullYear()}${p(d.getMonth() + 1)}${p(d.getDate())}-${p(d.getHours())}${p(d.getMinutes())}${p(d.getSeconds())}`;
  return {
    number: `LOCAL-${stamp}`,
    segment: "",
    customer: "",
    customerNo: "",
    model: "",
    serial: "",
    stock: "",
    description: "",
    assignedTech: techId,
    notes: "",
    reportText: "",
    source: "file",
    clockState: "idle",
    updatedUtc: "",
    folder: "",
    media: [],
    clock: [],
    key: "",
    label: "(new)",
    clockLines: [],
  };
}

type Field = "number" | "segment" | "customer" | "customerNo" | "model" | "serial" | "stock" | "description";

const FIELDS: [Field, string][] = [
  ["number", "WO number"],
  ["segment", "Segment"],
  ["customer", "Customer"],
  ["customerNo", "Customer #"],
  ["model", "Model"],
  ["serial", "Serial"],
  ["stock", "Stock"],
  ["description", "Complaint"],
];

export interface WorkOrdersProps {
  data: WoList | null;
  current: WorkOrder | null;
  /** Pick (or un-pick) the open work order; the shell persists it and fills the job strip. */
  onSelect: (wo: WorkOrder | null) => void;
  /** Field edits to the open work order (not saved until Save). */
  onEdit: (wo: WorkOrder) => void;
  /** Re-list and select `key`. */
  reload: (key?: string) => Promise<void>;
  onClearJob: () => Promise<void>;
  onShopSettings: () => void;
}

/** Port of WorkOrderControl.cs: list, header fields, notes, report, pictures, shop clock. */
export function WorkOrders({ data, current, onSelect, onEdit, reload, onClearJob, onShopSettings }: WorkOrdersProps) {
  const [status, setStatus] = useState(IDLE_STATUS);
  const [busy, setBusy] = useState(false);
  const [media, setMedia] = useState("");

  function edit(field: Field | "notes", value: string) {
    if (current) onEdit({ ...current, [field]: value });
  }

  /** Trim the header like WorkOrderControl.Flush; notes stay as typed. */
  function flushed(c: WorkOrder): WorkOrder {
    const out = { ...c };
    for (const [f] of FIELDS) out[f] = out[f].trim();
    return out;
  }

  async function run(action: (wo: WorkOrder) => Promise<string | null>) {
    if (!current) {
      setStatus("Pick a work order first.");
      return;
    }
    setBusy(true);
    try {
      const msg = await action(flushed(current));
      if (msg !== null) setStatus(msg);
    } catch (e) {
      setStatus(String(e));
    } finally {
      setBusy(false);
    }
  }

  /** After a write: re-list, then show the saved copy. */
  async function settle(saved: WorkOrder) {
    await reload(saved.key);
    onSelect(saved);
  }

  const save = () =>
    run(async (wo) => {
      const saved = await woSave(wo);
      await settle(saved);
      return `Saved WO ${saved.key}`;
    });

  const share = () =>
    run(async (wo) => {
      const r = await woShare(wo);
      await settle(r.wo);
      return `Shared WO ${r.wo.key} → ${r.dest}`;
    });

  const clock = (kind: "logon" | "logoff") =>
    run(async (wo) => {
      const saved = await woClock(wo, kind);
      await settle(saved);
      return `Shop ${kind} saved.`;
    });

  const addPhoto = () =>
    run(async (wo) => {
      const r = await woAddPhotos(wo);
      if (!r) return null;
      await settle(r.wo);
      return `Attached ${r.count} file(s).`;
    });

  const openMedia = (name: string) =>
    run(async (wo) => {
      if (!wo.folder) return "Save the work order first.";
      await woOpenMedia(wo.folder, name);
      return null;
    });

  async function clearJob() {
    try {
      await onClearJob();
      setStatus("Cleared saved job (customer and work order).");
    } catch (e) {
      setStatus(String(e));
    }
  }

  function newOrder() {
    onSelect(blankWorkOrder(data?.techId ?? ""));
    setStatus("New local work order. Replace LOCAL-… with the real WO number when you have it.");
  }

  const orders = data?.orders ?? [];
  const isListed = (o: WorkOrder) => current !== null && o.key !== "" && o.key === current.key;

  return (
    <section className="wo">
      <div className="wo-left">
        <div className="bar">
          <button onClick={newOrder}>New</button>
          <button onClick={() => void reload(current?.key)}>Refresh</button>
          <button onClick={() => void clearJob()}>Clear job</button>
        </div>
        <ul className="hits" role="listbox" aria-label="Work orders" tabIndex={0}>
          {orders.map((o) => (
            <li
              key={o.key}
              role="option"
              aria-selected={isListed(o)}
              className={isListed(o) ? "hit sel" : "hit"}
              onClick={() => {
                onSelect(o);
                setStatus(IDLE_STATUS);
              }}
            >
              <span className="title">{o.label}</span>
            </li>
          ))}
          {orders.length === 0 && <li className="empty">No work orders yet. Press New.</li>}
        </ul>
      </div>
      <div className="wo-right">
        {data && data.conflicts.length > 0 && (
          <div className="notice">
            {data.conflicts.length} work order(s) differ between this PC and the shop folder. The
            newer copy is shown; both are still on disk:
            <ul>
              {data.conflicts.map((c) => (
                <li key={c}>{c}</li>
              ))}
            </ul>
          </div>
        )}
        <div className="bar">
          <button disabled={busy} onClick={save}>
            Save
          </button>
          <button disabled={busy} onClick={share}>
            Share with shop
          </button>
          <button disabled={busy} onClick={() => clock("logon")}>
            Log on
          </button>
          <button disabled={busy} onClick={() => clock("logoff")}>
            Log off
          </button>
          <button disabled={busy} onClick={addPhoto}>
            Add photo
          </button>
          <button onClick={onShopSettings}>Shop settings</button>
        </div>
        {current ? (
          <div className="wo-form">
            {FIELDS.map(([f, label]) => (
              <label key={f} className={f === "description" ? "wide" : undefined}>
                {label}
                <input value={current[f]} onChange={(e) => edit(f, e.target.value)} />
              </label>
            ))}
            <label className="wide">
              Notes
              <textarea rows={5} value={current.notes} onChange={(e) => edit("notes", e.target.value)} />
            </label>
            <label className="wide">
              Report
              <pre className="detail mini">{current.reportText}</pre>
            </label>
            <div className="field">
              <span>Pictures (double-click to open)</span>
              <ul
                className="hits mini"
                role="listbox"
                aria-label="Pictures"
                tabIndex={0}
                onKeyDown={(e) => e.key === "Enter" && media && openMedia(media)}
              >
                {current.media.map((m) => (
                  <li
                    key={m}
                    role="option"
                    aria-selected={m === media}
                    className={m === media ? "hit sel" : "hit"}
                    onClick={() => setMedia(m)}
                    onDoubleClick={() => openMedia(m)}
                  >
                    <span className="title">{m}</span>
                  </li>
                ))}
                {current.media.length === 0 && <li className="empty">No pictures yet. Press Add photo.</li>}
              </ul>
            </div>
            <label>
              Clock
              <pre className="detail mini">
                {current.clockLines.length > 0
                  ? current.clockLines.join("\n")
                  : "No clock events yet. Log on and Log off write a shop timestamp."}
              </pre>
            </label>
          </div>
        ) : (
          <p className="muted">Pick a work order on the left, or press New.</p>
        )}
        <div className="status" role="status">
          {status}
        </div>
      </div>
    </section>
  );
}
