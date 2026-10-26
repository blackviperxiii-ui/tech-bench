import {
  cheatsheet,
  explainCode,
  jobTitle,
  kbOpen,
  kbSearch,
  shopSettingsGet,
  shopSettingsSave,
  smokeReport,
  woList,
  woSave,
} from "./api";
import { blankWorkOrder } from "./WorkOrders";

/** Known result from the shipped KB (same case as tb-core's shipped_field_database test). */
export const SMOKE_QUERY = "1AVPT";
export const SMOKE_KIND = "CODE";
export const SMOKE_EXPECT = "Sensor Failure 1AVPT";

/**
 * `--smoke`: the UI calls the real Tauri commands and reports back: a KB search for a known
 * code, the cheat sheet title, the SPN 5246 explainer header, the open-file guard refusing a
 * program and, with a scratch TECHBENCH_HOME, shop settings and a work order saved and read back.
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
    let guard = "";
    try {
      await kbOpen("smoke-check.exe");
    } catch (e) {
      guard = String(e);
    }
    const guardOk = guard.includes("won't open program or script files");
    let woOk = true;
    let woText = "work orders: skipped (no TECHBENCH_HOME)";
    if (workOrders) {
      await shopSettingsSave(" smoke tech. ", "", "");
      const tech = (await shopSettingsGet()).techId;
      const wo = { ...blankWorkOrder(tech), number: "SMOKE-1", segment: "01", customer: "Smoke Co" };
      const saved = await woSave(wo);
      const list = await woList();
      const listed = list.orders.find((o) => o.key === "SMOKE-1-01");
      const tag = await jobTitle(saved, "", "");
      woOk =
        tech === "smoke-tech" &&
        saved.key === "SMOKE-1-01" &&
        listed?.label === "SMOKE-1-01  Smoke Co" &&
        list.syncChip === "Sync · smoke-tech · click" &&
        tag === "WO SMOKE-1-01";
      woText = `tech: "${tech}"; work order: "${listed?.label ?? "(not listed)"}"; chip: "${list.syncChip}"; tag: "${tag}"`;
    }
    await smokeReport(
      searchOk && sheetOk && codeOk && guardOk && woOk,
      `search ${SMOKE_KIND} "${SMOKE_QUERY}": ${r.total} hit(s), first "${first}"; ` +
        `cheat sheet: "${sheet[0]?.text ?? ""}"; explain: "${codeLine}"; ` +
        `open guard: ${guardOk ? "refused .exe" : `"${guard}"`}; ${woText}`,
    );
  } catch (e) {
    await smokeReport(false, `smoke failed: ${String(e)}`);
  }
}
