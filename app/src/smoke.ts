import { cheatsheet, explainCode, kbSearch, smokeReport, woList, woSave } from "./api";
import { blankWorkOrder } from "./WorkOrders";

/** Known result from the shipped KB (same case as tb-core's shipped_field_database test). */
export const SMOKE_QUERY = "1AVPT";
export const SMOKE_KIND = "CODE";
export const SMOKE_EXPECT = "Sensor Failure 1AVPT";

/**
 * `--smoke`: the UI calls the real Tauri commands and reports back: a KB search for a known
 * code, the cheat sheet title, the SPN 5246 explainer header and, with a scratch TECHBENCH_HOME,
 * a work order saved and listed back.
 */
export async function runSmoke(workOrders: boolean) {
  try {
    const r = await kbSearch(SMOKE_QUERY, SMOKE_KIND);
    const first = r.hits[0]?.title ?? "(no hits)";
    const searchOk = r.total >= 1 && first.includes(SMOKE_EXPECT);
    const sheet = await cheatsheet();
    const sheetOk = sheet[0]?.text === "Tech Bench quick reference";
    const code = await explainCode(5246, 0);
    const codeLine = code.text.split(/\r?\n/)[1] ?? "";
    const codeOk = codeLine.startsWith("SPN 5246   FMI 0");
    let woOk = true;
    let woText = "work orders: skipped (no TECHBENCH_HOME)";
    if (workOrders) {
      const wo = { ...blankWorkOrder("smoke"), number: "SMOKE-1", segment: "01", customer: "Smoke Co" };
      const saved = await woSave(wo);
      const listed = (await woList()).orders.find((o) => o.key === "SMOKE-1-01");
      woOk = saved.key === "SMOKE-1-01" && listed?.label === "SMOKE-1-01  Smoke Co";
      woText = `work order: "${listed?.label ?? "(not listed)"}"`;
    }
    await smokeReport(
      searchOk && sheetOk && codeOk && woOk,
      `search ${SMOKE_KIND} "${SMOKE_QUERY}": ${r.total} hit(s), first "${first}"; ` +
        `cheat sheet: "${sheet[0]?.text ?? ""}"; explain: "${codeLine}"; ${woText}`,
    );
  } catch (e) {
    await smokeReport(false, `smoke failed: ${String(e)}`);
  }
}
