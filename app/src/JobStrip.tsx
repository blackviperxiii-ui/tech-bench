import type { WoList, WorkOrder } from "./api";
import { WEB } from "./platform";

export interface Header {
  customer: string;
  model: string;
  serial: string;
}

interface Props {
  data: WoList | null;
  current: WorkOrder | null;
  header: Header;
  onHeader: (h: Header) => void;
  onPick: (wo: WorkOrder | null) => void;
  /** The last shop sync's chip and whether it found conflicts; before a sync, the WO list's. */
  sync: { chip: string; conflict: boolean } | null;
  onSync: () => void;
  onSearchJob: () => void;
  onAddCode: () => void;
  onAddNote: () => void;
  onAddFile: () => void;
}

/** Port of the ShellForm job strip: WO pick list, customer / model / serial, sync chip. */
export function JobStrip({
  data,
  current,
  header,
  onHeader,
  onPick,
  sync,
  onSync,
  onSearchJob,
  onAddCode,
  onAddNote,
  onAddFile,
}: Props) {
  const orders = data?.orders ?? [];
  const rows = data?.pickRows ?? ["(no work order)"];
  // JobStrip.PickItems: the open order's row (key ignoring case), else the no-work-order row.
  const want = current?.key.toUpperCase() ?? "";
  const sel = want ? orders.findIndex((o) => o.key.toUpperCase() === want) + 1 : 0;

  const field = (name: keyof Header, label: string, width: string) => (
    <label className="job-field">
      {label}
      <input
        style={{ width }}
        value={header[name]}
        onChange={(e) => onHeader({ ...header, [name]: e.target.value })}
      />
    </label>
  );

  return (
    <div className="job" role="group" aria-label="Job">
      <label className="job-field">
        WO
        <select
          className="job-wo"
          value={sel}
          onChange={(e) => {
            const row = Number(e.target.value);
            // JobStrip.Picked: row 0 and stale rows are no order.
            onPick(row > 0 ? (orders[row - 1] ?? null) : null);
          }}
        >
          {rows.map((r, i) => (
            <option key={i} value={i}>
              {r}
            </option>
          ))}
        </select>
      </label>
      {field("customer", "Customer", "140px")}
      {field("model", "Model", "120px")}
      {field("serial", "Serial", "130px")}
      {!WEB && (
        <button className={sync?.conflict ? "job-sync conflict" : "job-sync"} onClick={onSync} title="Shop sync">
          {sync?.chip ?? (data ? data.syncChip : "Sync: idle")}
        </button>
      )}
      <button className="job-btn" onClick={onSearchJob}>
        Search this job
      </button>
      {/* The web build's KB is read-only: it is part of the app, not a shop folder. */}
      {!WEB && (
        <>
          <button className="job-btn" onClick={onAddCode}>
            Add code to KB
          </button>
          <button className="job-btn" onClick={onAddNote}>
            Add note
          </button>
          <button className="job-btn" onClick={onAddFile}>
            Add file
          </button>
        </>
      )}
    </div>
  );
}
