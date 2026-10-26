import {
  busCall,
  busCaps,
  busReport,
  sessionSave,
  cheatsheet,
  type BusAdapter,
  type BusSnap,
  explainCode,
  historySummary,
  jobTitle,
  kbCodeSeed,
  kbOpen,
  kbSearch,
  shopSettingsGet,
  shopSettingsSave,
  smokeReport,
  woAttachReport,
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
 * program and, with a scratch TECHBENCH_HOME, shop settings and a work order saved and read back
 * and the unit history for it. With the bridge on its fake adapter, it also connects over the
 * sidecar, reads the engine's RPM and codes, and checks a TSC1 hold is refused while Safety is off.
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
    // Add code seed for the found hit (read-only: no saved user code yet, so the title seeds).
    const seed = await kbCodeSeed("CODE", first);
    const seedOk = seed.title === first && seed.code === "";
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
      // Scratch home: no saved sessions yet, so unit history says so.
      const hist = (await historySummary(tag)).text.split("\n")[0];
      // Attach report: the text lands on the order and as a media file in its packet.
      const att = await woAttachReport(saved, "TECH BENCH — DIAGNOSTIC REPORT\r\n");
      const attOk = att.reportText.startsWith("TECH BENCH") && att.media.includes("diagnostic-report.txt");
      woOk =
        tech === "smoke-tech" &&
        saved.key === "SMOKE-1-01" &&
        listed?.label === "SMOKE-1-01  Smoke Co" &&
        list.syncChip === "Sync · smoke-tech · click" &&
        tag === "WO SMOKE-1-01" &&
        hist === "No saved sessions yet." &&
        attOk;
      woText = `tech: "${tech}"; work order: "${listed?.label ?? "(not listed)"}"; chip: "${list.syncChip}"; tag: "${tag}"; history: "${hist}"; attach: ${attOk ? "diagnostic-report.txt" : JSON.stringify(att.media)}`;
    }
    const bus = await smokeBus(workOrders);
    await smokeReport(
      searchOk && sheetOk && codeOk && guardOk && seedOk && woOk && bus.ok,
      `search ${SMOKE_KIND} "${SMOKE_QUERY}": ${r.total} hit(s), first "${first}"; ` +
        `cheat sheet: "${sheet[0]?.text ?? ""}"; explain: "${codeLine}"; ` +
        `open guard: ${guardOk ? "refused .exe" : `"${guard}"`}; code seed: "${seed.title}"; ${woText}; ${bus.text}`,
    );
  } catch (e) {
    await smokeReport(false, `smoke failed: ${String(e)}`);
  }
}

/** Live bus through the bridge sidecar, only on its fake adapter (never real hardware). */
async function smokeBus(scratchHome: boolean): Promise<{ ok: boolean; text: string }> {
  const caps = await busCaps();
  if (!caps.fake) return { ok: true, text: `live bus: skipped (${caps.available ? "no fake adapter" : caps.reason})` };
  if (!caps.available) return { ok: false, text: `live bus: ${caps.reason}` };
  const hello = await busCall<{ bridge: string; pointerBits: number }>("hello");
  const adapters = await busCall<BusAdapter[]>("adapters");
  await busCall("connect", { adapter: "FAKE" });
  let s: BusSnap | null = null;
  for (let i = 0; i < 100; i++) {
    s = await busCall<BusSnap>("snapshot");
    if (s.rpm === 800 && s.active.length > 0) break;
    await new Promise((r) => setTimeout(r, 100));
  }
  const held = await busCall<number>("tsc1", { rpm: 800 });
  const report = await busReport("SMOKE UNIT", "");
  const reportOk = report.includes("Job:      SMOKE UNIT\r\n") && report.includes("  RPM:      800\r\n");
  // Save writes into the session folder, so only under a scratch TECHBENCH_HOME. The saved
  // DTCs then show up as unit history for the same job.
  let saveText = "save: skipped (no TECHBENCH_HOME)";
  let saveOk = true;
  if (scratchHome) {
    const saved = await sessionSave("SMOKE UNIT");
    const hist = (await historySummary("SMOKE UNIT")).text;
    saveOk = /SMOKE_UNIT_session_\d{8}_\d{6}\.txt$/.test(saved.path) && hist.includes("SPN 1761");
    saveText = `saved "${saved.path.split(/[\\/]/).pop() ?? ""}"; history ${hist.includes("SPN 1761") ? "lists SPN 1761" : `"${hist.split("\n")[0]}"`}`;
  }
  await busCall("disconnect");
  const code = s?.active[0]?.text ?? "(no active code)";
  const ok =
    adapters[0]?.id === "FAKE" &&
    s?.hud.rpm === "RPM: 800" &&
    code.startsWith("SPN 1761  FMI 9") &&
    held === 0 &&
    reportOk &&
    saveOk;
  return {
    ok,
    text: `live bus: bridge ${hello.bridge} pointerBits ${hello.pointerBits}; ${s?.hud.rpm ?? "(no snapshot)"}; active "${code}"; TSC1 with Safety off held ${held}; report ${reportOk ? "ok" : "wrong"}; ${saveText}`,
  };
}
