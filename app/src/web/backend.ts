import { tb } from "./tb";
import { getJob, packets, putPacket, updateJob } from "./store";

/** The UTC time as .NET's round-trip "o" (seven fraction digits), as the desktop stamps saves. */
function utcNow(): string {
  return new Date().toISOString().replace(/\.(\d{3})Z$/, (_, ms: string) => `.${ms}0000Z`);
}

const s = (v: unknown) => String(v ?? "");

/** A saved work order: tb-core checks and stamps it, then it is kept in this browser. */
async function keep(cmd: string, args: Record<string, unknown>): Promise<unknown> {
  const r = await tb<{ id: string; packet: string; wo: unknown }>(cmd, { ...args, now: utcNow() });
  await putPacket(r.id, r.packet);
  return r.wo;
}

/**
 * The desktop commands the UI calls, answered in the browser. tb-core (wasm) does the work; the
 * rest are the parts that need a PC: the shop folders, files on disk and the adapter.
 */
export async function webInvoke(cmd: string, args: Record<string, unknown>): Promise<unknown> {
  switch (cmd) {
    case "smoke_mode":
      return { on: false, workOrders: false };
    case "wo_list":
      return tb(cmd, { packets: await packets() });
    case "wo_save":
    case "wo_clock":
      return keep(cmd, args);
    case "job_get":
      return getJob();
    case "job_set_header":
      return updateJob({ customer: s(args.customer).trim(), model: s(args.model).trim(), serial: s(args.serial).trim() });
    case "job_set_wo":
      return updateJob({ lastWo: s(args.key).trim() });
    case "job_clear":
      return updateJob({ customer: "", lastWo: "" });
    case "job_title": {
      const r = await tb<{ tag: string; title: string }>(cmd, { ...args, version: __APP_VERSION__ });
      document.title = r.title;
      return r.tag;
    }
    case "bus_caps":
      return {
        available: false,
        fake: false,
        reason: "Live bus needs the Windows build: the INLINE 7 adapter only has 32-bit Windows RP1210 drivers.",
      };
    case "history_summary":
      return { text: "Unit history comes from saved bus sessions, which need the Windows build.", folder: "" };
    default:
      return tb(cmd, args);
  }
}
