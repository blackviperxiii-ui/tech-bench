import type { BusSnap } from "../api";

/**
 * Shot: the INLINE 7 state as text lines for the PNG. The WinForms Shot copied the window's
 * pixels; a webview cannot copy itself without a new dependency, so this draws the same data.
 */
export function shotLines(s: BusSnap | null, jobTag: string, when: string): string[] {
  const codes = (label: string, list: BusSnap["active"]) => [
    label,
    ...(list.length === 0 ? ["  (none)"] : list.map((d) => `  ${d.text}`)),
  ];
  return [
    `Tech Bench — INLINE 7  ${when}`,
    jobTag.trim() ? `Job  ${jobTag}` : "Job  (none)",
    "",
    s?.hud.status ?? "Not connected",
    s?.hud.rpm ?? "RPM: —",
    s?.hud.lamps ?? "Lamps: —",
    s?.hud.def ?? "DEF: —",
    ...(s?.hud.mon ? [s.hud.mon] : []),
    ...(s?.aftText ? s.aftText.split(/\r?\n/) : []),
    "",
    ...codes("Active codes (DM1)", s?.active ?? []),
    "",
    ...codes("Previously active (DM2)", s?.prev ?? []),
  ];
}

const FONT_PX = 15;
const LINE_PX = 21;
const PAD_PX = 18;

/** Draws the lines black on white in a fixed monospace font and returns the PNG bytes. */
export async function renderShot(lines: string[]): Promise<Uint8Array> {
  const canvas = document.createElement("canvas");
  const ctx = canvas.getContext("2d");
  if (!ctx) throw new Error("This system cannot draw the shot (no 2D canvas).");
  const font = `${FONT_PX}px ui-monospace, Consolas, "DejaVu Sans Mono", monospace`;
  ctx.font = font;
  const width = Math.max(640, ...lines.map((l) => Math.ceil(ctx.measureText(l).width)));
  canvas.width = width + PAD_PX * 2;
  canvas.height = lines.length * LINE_PX + PAD_PX * 2;
  // Resizing resets the context, so set the font again.
  ctx.fillStyle = "#ffffff";
  ctx.fillRect(0, 0, canvas.width, canvas.height);
  ctx.fillStyle = "#1c2026";
  ctx.font = font;
  ctx.textBaseline = "top";
  lines.forEach((l, i) => {
    if (i === 0) ctx.font = `bold ${font}`;
    ctx.fillText(l, PAD_PX, PAD_PX + i * LINE_PX);
    if (i === 0) ctx.font = font;
  });
  const blob = await new Promise<Blob | null>((ok) => canvas.toBlob(ok, "image/png"));
  if (!blob) throw new Error("This system could not encode the shot as PNG.");
  return new Uint8Array(await blob.arrayBuffer());
}
