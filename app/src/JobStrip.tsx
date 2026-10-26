import type { WoList, WorkOrder } from "./api";

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
  onSync: () => void;
  onSearchJob: () => void;
}

/** Port of the ShellForm job strip: WO pick list, customer / model / serial, sync chip. */
export function JobStrip({ data, current, header, onHeader, onPick, onSync, onSearchJob }: Props) {
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
      <button className="job-sync" onClick={onSync} title="Work orders">
        {data ? data.syncChip : "Sync: idle"}
      </button>
      <button className="job-btn" onClick={onSearchJob}>
        Search this job
      </button>
    </div>
  );
}
