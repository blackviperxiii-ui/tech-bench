import { cheatsheet, explainCode, kbSearch, smokeReport } from "./api";

/** Known result from the shipped KB (same case as tb-core's shipped_field_database test). */
export const SMOKE_QUERY = "1AVPT";
export const SMOKE_KIND = "CODE";
export const SMOKE_EXPECT = "Sensor Failure 1AVPT";

/**
 * `--smoke`: the UI calls the real Tauri commands and reports back: a KB search for a known
 * code, the cheat sheet title, and the SPN 5246 explainer header.
 */
export async function runSmoke() {
  try {
    const r = await kbSearch(SMOKE_QUERY, SMOKE_KIND);
    const first = r.hits[0]?.title ?? "(no hits)";
    const searchOk = r.total >= 1 && first.includes(SMOKE_EXPECT);
    const sheet = await cheatsheet();
    const sheetOk = sheet[0]?.text === "Tech Bench quick reference";
    const code = await explainCode(5246, 0);
    const codeLine = code.text.split(/\r?\n/)[1] ?? "";
    const codeOk = codeLine.startsWith("SPN 5246   FMI 0");
    await smokeReport(
      searchOk && sheetOk && codeOk,
      `search ${SMOKE_KIND} "${SMOKE_QUERY}": ${r.total} hit(s), first "${first}"; ` +
        `cheat sheet: "${sheet[0]?.text ?? ""}"; explain: "${codeLine}"`,
    );
  } catch (e) {
    await smokeReport(false, `smoke failed: ${String(e)}`);
  }
}
