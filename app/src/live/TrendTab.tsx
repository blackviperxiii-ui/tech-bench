import { useEffect, useState } from "react";
import { busCall, busExport } from "../api";

/** The bridge's chart model for one channel (tb-core `TrendChart`); heights run 0 (bottom) to 1. */
interface Chart {
  label: string;
  notice: string;
  ticks: string[];
  heights: (number | null)[];
  /** 2 red lamp, 1 amber, 0 neither. */
  lamps: number[];
  first: number;
  last: number;
  span: string;
}

interface Trend {
  count: number;
  channels: string[];
  chart: Chart;
}

const TREND_MS = 1000;
/** Same help text as the WinForms Trend tab. */
const HELP =
  "One sample a second, last 30 minutes. Shaded bands are where the red (pink) or amber (yellow) lamp was on — so you can see what a reading did before the lamp came on.";

// Plot box in SVG units, with TrendChart.cs's paddings.
const W = 900;
const H = 320;
const PAD = { left: 58, right: 12, top: 10, bottom: 24 };
const PW = W - PAD.left - PAD.right;
const PH = H - PAD.top - PAD.bottom;

function hms(ms: number): string {
  const d = new Date(ms);
  const p = (n: number) => String(n).padStart(2, "0");
  return `${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`;
}

/** TrendChart.OnPaint as SVG: lamp bands, grid and axis labels, the line (broken at gaps), times. */
function TrendSvg({ c }: { c: Chart }) {
  const n = c.heights.length;
  const x = (i: number) => PAD.left + (n <= 1 ? 0 : (i / (n - 1)) * (PW - 1));
  const y = (h: number) => PAD.top + PH - h * PH;

  const bands: { x: number; w: number; red: boolean }[] = [];
  for (let i = 0; i < n; i++) {
    const lamp = c.lamps[i] ?? 0;
    if (lamp === 0) continue;
    let j = i;
    while (j + 1 < n && c.lamps[j + 1] === lamp) j++;
    const x0 = x(i);
    bands.push({ x: x0, w: Math.max(1, x(Math.min(j + 1, n - 1)) - x0), red: lamp === 2 });
    i = j;
  }

  // Runs of published values; a lone point is a dot, as the WinForms chart draws it.
  const runs: string[][] = [];
  let cur: string[] = [];
  c.heights.forEach((h, i) => {
    if (h === null) {
      if (cur.length) runs.push(cur);
      cur = [];
    } else cur.push(`${x(i).toFixed(1)},${y(h).toFixed(1)}`);
  });
  if (cur.length) runs.push(cur);

  return (
    <svg className="trend-chart" viewBox={`0 0 ${W} ${H}`} role="img" aria-label={`${c.label} trend`}>
      <rect className="trend-frame" x={PAD.left} y={PAD.top} width={PW} height={PH} />
      {c.notice ? (
        <text className="trend-muted" x={PAD.left + PW / 2} y={PAD.top + PH / 2} textAnchor="middle">
          {c.notice}
        </text>
      ) : (
        <>
          {bands.map((b, i) => (
            <rect key={i} className={b.red ? "trend-red" : "trend-amber"} x={b.x} y={PAD.top + 1} width={b.w} height={PH - 1} />
          ))}
          {[1, 2, 3].map((i) => (
            <line key={i} className="trend-grid" x1={PAD.left + 1} x2={PAD.left + PW - 1} y1={PAD.top + (PH * i) / 4} y2={PAD.top + (PH * i) / 4} />
          ))}
          {c.ticks.map((t, i) => (
            <text key={i} className="trend-tick" x={PAD.left - 6} y={PAD.top + (PH * i) / 4 + 4} textAnchor="end">
              {t}
            </text>
          ))}
          <text className="trend-muted" x={PAD.left + 4} y={PAD.top + 14}>
            {c.label}
          </text>
          {runs.map((r, i) =>
            r.length === 1 ? (
              <rect key={i} className="trend-dot" x={Number(r[0].split(",")[0])} y={Number(r[0].split(",")[1]) - 1} width={2} height={2} />
            ) : (
              <polyline key={i} className="trend-line" points={r.join(" ")} />
            ),
          )}
          <text className="trend-tick" x={PAD.left} y={H - 6}>
            {hms(c.first)}
          </text>
          <text className="trend-muted" x={PAD.left + PW / 2} y={H - 6} textAnchor="middle">
            {c.span}
          </text>
          <text className="trend-tick" x={PAD.left + PW} y={H - 6} textAnchor="end">
            {hms(c.last)}
          </text>
        </>
      )}
    </svg>
  );
}

/** Trend tab (Inline7Control.BuildTrendTab): Channel, Export CSV, Clear history and the chart. */
export function TrendTab({
  jobTag,
  run,
  addLog,
}: {
  jobTag: string;
  run: <T>(cmd: string, args?: Record<string, unknown>) => Promise<T | null>;
  addLog: (lines: string[]) => void;
}) {
  const [channel, setChannel] = useState(0);
  const [trend, setTrend] = useState<Trend | null>(null);

  useEffect(() => {
    let gone = false;
    const tick = () =>
      void busCall<Trend>("trend", { channel })
        .then((t) => {
          if (!gone) setTrend(t);
        })
        .catch(() => {
          /* The next tick tries again. */
        });
    const t = window.setInterval(tick, TREND_MS);
    tick();
    return () => {
      gone = true;
      window.clearInterval(t);
    };
  }, [channel]);

  async function exportCsv() {
    try {
      addLog([`Trend exported ${await busExport("trend", jobTag)}`]);
    } catch (e) {
      // The bridge says "No trend samples yet." before the first second, as the WinForms log does.
      addLog([String(e)]);
    }
  }

  const names = trend?.channels ?? ["RPM", "Coolant °C", "Oil kPa", "Battery V", "Fuel L/h"];
  return (
    <div className="trend-tab">
      <div className="bar">
        <label className="check">
          Channel
          <select aria-label="Channel" value={channel} onChange={(e) => setChannel(Number(e.target.value))}>
            {names.map((n, i) => (
              <option key={n} value={i}>
                {n}
              </option>
            ))}
          </select>
        </label>
        <button type="button" onClick={() => void exportCsv()}>
          Export CSV
        </button>
        <button type="button" onClick={() => void run("clearHistory")}>
          Clear history
        </button>
      </div>
      {trend && <TrendSvg c={trend.chart} />}
      <p className="detail mini trend-help">{HELP}</p>
    </div>
  );
}
