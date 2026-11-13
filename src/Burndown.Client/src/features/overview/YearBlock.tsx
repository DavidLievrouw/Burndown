import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { formatMoney, formatMonthShort } from "../../lib/format";
import { fetchYearOverview, type YearOverviewResponse } from "./api";
import { describeError } from "./ErrorPanel";

function resultTone(value: number): string {
  return value < 0 ? "text-bad" : "text-ok";
}

function YearBody({ data }: { data: YearOverviewResponse }) {
  if (data.months.length === 0) {
    return <p className="text-sm text-muted">No previous data</p>;
  }
  return (
    <div className="flex flex-wrap items-center gap-x-8 gap-y-3">
      <div>
        <div className="text-xs tracking-wide text-muted uppercase">Total</div>
        <div className={`text-2xl font-semibold tabular-nums ${resultTone(data.total)}`}>{formatMoney(data.total)}</div>
      </div>
      <ul className="flex flex-wrap gap-2" aria-label="Result per month">
        {data.months.map((entry) => (
          <li
            key={entry.month}
            className="min-w-24 rounded-md border border-border bg-surface px-2.5 py-1.5"
            data-testid="year-month"
          >
            <div className="text-xs text-muted">{formatMonthShort(entry.month)}</div>
            <div className={`text-sm font-medium tabular-nums ${resultTone(entry.result)}`}>
              {formatMoney(entry.result)}
            </div>
          </li>
        ))}
      </ul>
    </div>
  );
}

/** The result of the months before the viewed month; it loads and fails independently of the month overview. */
export function YearBlock({ month }: { month: string | null }) {
  const query = useQuery({
    queryKey: ["year-overview", month],
    queryFn: ({ signal }) => fetchYearOverview(month, signal),
    placeholderData: keepPreviousData,
    enabled: month !== null,
  });
  const data = query.data;
  const title = data
    ? data.month.endsWith("-01")
      ? `Previous year ${data.year}, result`
      : `Year ${data.year}, result so far`
    : "Year result so far";

  return (
    <section
      aria-label="Year overview"
      aria-busy={query.isFetching}
      className={`rounded-lg border border-border bg-surface-raised px-4 py-3 transition-opacity ${query.isPlaceholderData ? "opacity-60" : ""}`}
    >
      <h2 className="mb-2 text-sm font-semibold">{title}</h2>
      {query.isError ? (
        <div role="status" className="flex flex-wrap items-center gap-3 text-sm">
          <span className="text-bad">{describeError(query.error).title}.</span>
          <button
            type="button"
            onClick={() => void query.refetch()}
            className="rounded-md border border-border px-2 py-0.5 text-xs hover:border-accent"
          >
            Try again
          </button>
        </div>
      ) : data ? (
        <YearBody data={data} />
      ) : (
        <div className="h-12 animate-pulse rounded bg-surface" role="status" aria-label="Loading year overview" />
      )}
    </section>
  );
}
