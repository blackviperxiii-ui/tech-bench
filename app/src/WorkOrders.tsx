import { useCallback, useEffect, useState } from "react";
import { woClock, woList, woSave, woShare, type WorkOrder, type WoList } from "./api";

const IDLE_STATUS =
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

/** Port of WorkOrderControl.cs: list, header fields, notes, report, pictures, shop clock. */
export function WorkOrders() {
  const [data, setData] = useState<WoList | null>(null);
  const [current, setCurrent] = useState<WorkOrder | null>(null);
  const [status, setStatus] = useState(IDLE_STATUS);
  const [busy, setBusy] = useState(false);

  const reload = useCallback(async (selectKey?: string) => {
    try {
      const d = await woList();
      setData(d);
      if (selectKey) {
        const hit = d.orders.find((o) => o.key.toUpperCase() === selectKey.toUpperCase());
        if (hit) setCurrent(hit);
      }
    } catch (e) {
      setStatus(`Could not list work orders: ${String(e)}`);
    }
  }, []);

  useEffect(() => {
    void reload();
  }, [reload]);

  function edit(field: Field | "notes", value: string) {
    setCurrent((c) => (c ? { ...c, [field]: value } : c));
  }

  /** Trim the header like WorkOrderControl.Flush; notes stay as typed. */
  function flushed(c: WorkOrder): WorkOrder {
    const out = { ...c };
    for (const [f] of FIELDS) out[f] = out[f].trim();
    return out;
  }

  async function run(action: (wo: WorkOrder) => Promise<string>) {
    if (!current) {
      setStatus("Pick a work order first.");
      return;
    }
    setBusy(true);
    try {
      setStatus(await action(flushed(current)));
    } catch (e) {
      setStatus(String(e));
    } finally {
      setBusy(false);
    }
  }

  const save = () =>
    run(async (wo) => {
      const saved = await woSave(wo);
      await reload(saved.key);
      setCurrent(saved);
      return `Saved WO ${saved.key}`;
    });

  const share = () =>
    run(async (wo) => {
      const r = await woShare(wo);
      await reload(r.wo.key);
      setCurrent(r.wo);
      return `Shared WO ${r.wo.key} → ${r.dest}`;
    });

  const clock = (kind: "logon" | "logoff") =>
    run(async (wo) => {
      const saved = await woClock(wo, kind);
      await reload(saved.key);
      setCurrent(saved);
      return `Shop ${kind} saved.`;
    });

  function newOrder() {
    setCurrent(blankWorkOrder(data?.techId ?? ""));
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
        </div>
        <ul className="hits" role="listbox" aria-label="Work orders" tabIndex={0}>
          {orders.map((o) => (
            <li
              key={o.key}
              role="option"
              aria-selected={isListed(o)}
              className={isListed(o) ? "hit sel" : "hit"}
              onClick={() => {
                setCurrent(o);
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
            <label>
              Pictures
              <ul className="hits mini">
                {current.media.map((m) => (
                  <li key={m} className="hit">
                    <span className="title">{m}</span>
                  </li>
                ))}
              </ul>
            </label>
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
