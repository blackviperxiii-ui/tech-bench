import { kbSearch, smokeReport } from "./api";

/** Known result from the shipped KB (same case as tb-core's shipped_field_database test). */
export const SMOKE_QUERY = "1AVPT";
export const SMOKE_KIND = "CODE";
export const SMOKE_EXPECT = "Sensor Failure 1AVPT";

/** `--smoke`: the UI runs a real KB search through the Tauri command and reports back. */
export async function runSmoke() {
  try {
    const r = await kbSearch(SMOKE_QUERY, SMOKE_KIND);
    const first = r.hits[0]?.title ?? "(no hits)";
    const ok = r.total >= 1 && first.includes(SMOKE_EXPECT);
    await smokeReport(ok, `search ${SMOKE_KIND} "${SMOKE_QUERY}": ${r.total} hit(s), first "${first}"`);
  } catch (e) {
    await smokeReport(false, `search failed: ${String(e)}`);
  }
}
