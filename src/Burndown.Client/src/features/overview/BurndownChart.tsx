import { CartesianGrid, Line, LineChart, ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { formatMoney, formatMoneyWhole } from "../../lib/format";
import type { BurndownChartData } from "./api";

const COLORS = { breakEven: "#9097a6", actual: "#6aa7ff", today: "#e6e8ee", grid: "#2a2f3a" } as const;

function lastActual(chart: BurndownChartData): number | null {
  for (let index = chart.points.length - 1; index >= 0; index--) {
    const actual = chart.points[index]?.actual;
    if (actual !== null && actual !== undefined) {
      return actual;
    }
  }
  return null;
}

function summarize(chart: BurndownChartData): string {
  const actual = lastActual(chart);
  const breakEven = chart.points[chart.points.length - 1]?.breakEven ?? 0;
  const status =
    actual === null ? "No days have started yet." : `The remaining budget so far is ${formatMoney(actual)}.`;
  return `Burndown chart of the month budget: the break-even line falls from the month budget to ${formatMoney(breakEven)}. Above it you are saving, below it you are overspending. ${status}`;
}

export function BurndownChart({ chart }: { chart: BurndownChartData }) {
  return (
    <figure className="rounded-lg border border-border bg-surface-raised p-4" aria-label="Burndown chart">
      <figcaption className="mb-2 flex items-center justify-between gap-4 text-sm">
        <span className="font-semibold">Budget burndown</span>
        <span className="flex items-center gap-4 text-xs text-muted" aria-hidden="true">
          <span
            className="flex items-center gap-1.5"
            title="Above this line you are saving, below it you are overspending."
          >
            <span
              className="inline-block h-0.5 w-4 border-t-2 border-dashed"
              style={{ borderColor: COLORS.breakEven }}
            />
            Break-even pace
          </span>
          <span className="flex items-center gap-1.5">
            <span className="inline-block h-0.5 w-4" style={{ backgroundColor: COLORS.actual }} />
            Remaining
          </span>
        </span>
      </figcaption>
      <p className="sr-only">{summarize(chart)}</p>
      <div className="h-72">
        <ResponsiveContainer width="100%" height="100%" initialDimension={{ width: 480, height: 288 }}>
          <LineChart data={chart.points} margin={{ top: 8, right: 16, bottom: 0, left: 0 }}>
            <CartesianGrid stroke={COLORS.grid} vertical={false} />
            <XAxis
              dataKey="day"
              type="number"
              domain={[0, "dataMax"]}
              tick={{ fill: COLORS.breakEven, fontSize: 12 }}
              stroke={COLORS.grid}
              tickCount={8}
              allowDecimals={false}
            />
            <YAxis
              tick={{ fill: COLORS.breakEven, fontSize: 12 }}
              stroke={COLORS.grid}
              width={72}
              tickFormatter={(value: number) => formatMoneyWhole(value)}
            />
            <Tooltip
              contentStyle={{ background: "#0f1115", border: `1px solid ${COLORS.grid}`, borderRadius: 6 }}
              labelStyle={{ color: "#e6e8ee" }}
              labelFormatter={(day) => `Day ${String(day)}`}
              formatter={(value) => (typeof value === "number" ? formatMoney(value) : String(value ?? ""))}
            />
            <ReferenceLine y={0} stroke={COLORS.grid} />
            {chart.todayDay === null ? null : (
              <ReferenceLine
                x={chart.todayDay}
                stroke={COLORS.today}
                strokeDasharray="4 4"
                label={{ value: "Today", position: "insideTopRight", fill: COLORS.today, fontSize: 12 }}
              />
            )}
            <Line
              name="Break-even pace"
              dataKey="breakEven"
              stroke={COLORS.breakEven}
              strokeDasharray="6 4"
              strokeWidth={2}
              dot={false}
              isAnimationActive={false}
            />
            <Line
              name="Remaining"
              dataKey="actual"
              stroke={COLORS.actual}
              strokeWidth={2.5}
              dot={false}
              connectNulls={false}
              isAnimationActive={false}
            />
          </LineChart>
        </ResponsiveContainer>
      </div>
      <details className="mt-3 text-sm">
        <summary className="cursor-pointer text-muted hover:text-text">Show chart data as a table</summary>
        <div className="mt-2 max-h-[max(16rem,min(36rem,calc(100vh-26rem)))] overflow-auto">
          <table className="w-full border-collapse text-xs">
            <caption className="sr-only">Burndown chart data</caption>
            <thead>
              <tr className="text-muted">
                <th scope="col" className="sticky top-0 bg-surface-raised px-2 py-1 text-left font-medium">
                  Day
                </th>
                <th scope="col" className="sticky top-0 bg-surface-raised px-2 py-1 text-right font-medium">
                  Break-even pace
                </th>
                <th scope="col" className="sticky top-0 bg-surface-raised px-2 py-1 text-right font-medium">
                  Remaining
                </th>
              </tr>
            </thead>
            <tbody>
              {chart.points.map((point) => (
                <tr key={point.day} className="border-t border-border/60">
                  <th scope="row" className="px-2 py-1 text-left font-normal">
                    {point.day}
                  </th>
                  <td className="px-2 py-1 text-right tabular-nums">{formatMoney(point.breakEven)}</td>
                  <td className="px-2 py-1 text-right tabular-nums">
                    {point.actual === null ? "–" : formatMoney(point.actual)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </figure>
  );
}
