import { formatMoney } from "../../lib/format";
import type { OverviewResponse } from "./api";

interface CardProps {
  label: string;
  value: string;
  note?: string;
  tone?: "ok" | "bad";
}

function Card({ label, value, note, tone }: CardProps) {
  const toneClass = tone === "ok" ? "text-ok" : tone === "bad" ? "text-bad" : "text-text";
  return (
    <div className="rounded-lg border border-border bg-surface-raised px-4 py-3">
      <dt className="text-xs tracking-wide text-muted uppercase">{label}</dt>
      <dd className={`mt-1 text-xl font-semibold tabular-nums ${toneClass}`}>{value}</dd>
      <dd className="min-h-4 text-xs text-muted">{note}</dd>
    </div>
  );
}

export function SummaryCards({ data }: { data: OverviewResponse }) {
  const { budget, totals } = data;
  return (
    <dl className="grid grid-cols-2 gap-3 lg:grid-cols-3 xl:grid-cols-6" aria-label="Month summary">
      <Card
        label="Month budget"
        value={formatMoney(budget.effective)}
        note={budget.overridden ? `Overridden (computed ${formatMoney(budget.computed)})` : "Computed"}
      />
      <Card label="Income, previous month" value={formatMoney(budget.income)} />
      <Card label="Installments, previous month" value={formatMoney(budget.installments)} />
      <Card label="Daily budget" value={formatMoney(totals.dailyBudget)} />
      <Card label="Spent" value={formatMoney(totals.spent)} />
      <Card label="Remaining" value={formatMoney(totals.remaining)} tone={totals.remaining < 0 ? "bad" : "ok"} />
    </dl>
  );
}
