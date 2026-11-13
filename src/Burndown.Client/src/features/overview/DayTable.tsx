import { formatDate, formatMoney, formatWeekday } from "../../lib/format";
import type { DayRow, OverviewResponse } from "./api";
import { amountBackground, perDayBackground } from "./colors";

function rowClass(day: DayRow): string {
  const classes = ["border-b border-border/60"];
  if (day.isToday) {
    classes.push("bg-accent/15 shadow-[inset_3px_0_0_var(--color-accent)]");
  } else if (day.isWeekend) {
    classes.push("bg-white/[0.04]");
  }
  if (day.isFuture) {
    classes.push("text-muted");
  }
  return classes.join(" ");
}

function Row({ day }: { day: DayRow }) {
  const hasAmount = day.amount !== 0;
  return (
    <tr className={rowClass(day)} aria-current={day.isToday ? "date" : undefined}>
      <th scope="row" className="px-3 py-1.5 text-left font-normal whitespace-nowrap">
        <span className="inline-block w-9 text-muted">{formatWeekday(day.date)}</span>
        {formatDate(day.date)}
        {day.isToday ? <span className="ml-2 text-xs font-semibold text-accent">Today</span> : null}
      </th>
      <td className="px-3 py-1.5">{day.description}</td>
      <td
        className="px-3 py-1.5 text-right tabular-nums whitespace-nowrap"
        style={{ backgroundColor: amountBackground(day.amountAlpha) }}
      >
        {hasAmount ? formatMoney(day.amount) : ""}
      </td>
      <td className="px-3 py-1.5 text-right tabular-nums whitespace-nowrap">{formatMoney(day.runningTotal)}</td>
      <td className="px-3 py-1.5 text-right tabular-nums whitespace-nowrap">{formatMoney(day.cumulativeBudget)}</td>
      <td
        className="px-3 py-1.5 text-right tabular-nums whitespace-nowrap"
        style={{ backgroundColor: perDayBackground(day.perDayTone, day.perDayAlpha) }}
      >
        {formatMoney(day.perDay)}
      </td>
    </tr>
  );
}

const headClass =
  "sticky top-0 z-10 bg-surface-raised px-3 py-2 text-xs font-medium tracking-wide text-muted uppercase";

export function DayTable({ data }: { data: OverviewResponse }) {
  const { days, totals } = data;
  const hasExpenses = days.some((day) => day.amount !== 0);
  return (
    <div>
      <table className="w-full border-collapse text-sm">
        <caption className="sr-only">Daily expenses and remaining budget</caption>
        <thead>
          <tr>
            <th scope="col" className={`${headClass} text-left`}>
              Date
            </th>
            <th scope="col" className={`${headClass} text-left`}>
              Description
            </th>
            <th scope="col" className={`${headClass} text-right`}>
              Amount
            </th>
            <th scope="col" className={`${headClass} text-right`}>
              Total
            </th>
            <th scope="col" className={`${headClass} text-right`}>
              Budget
            </th>
            <th scope="col" className={`${headClass} text-right`}>
              Per day
            </th>
          </tr>
        </thead>
        <tbody>
          {days.map((day) => (
            <Row key={day.date} day={day} />
          ))}
        </tbody>
        <tfoot>
          <tr className="border-t border-border bg-surface-raised font-semibold">
            <th scope="row" className="px-3 py-2 text-left" colSpan={2}>
              Month
            </th>
            <td className="px-3 py-2 text-right tabular-nums whitespace-nowrap">{formatMoney(totals.spent)}</td>
            <td />
            <td />
            <td className="px-3 py-2 text-right tabular-nums whitespace-nowrap">{formatMoney(totals.remaining)}</td>
          </tr>
        </tfoot>
      </table>
      {hasExpenses ? null : <p className="px-3 py-4 text-sm text-muted">No expenses in this month yet.</p>}
    </div>
  );
}
