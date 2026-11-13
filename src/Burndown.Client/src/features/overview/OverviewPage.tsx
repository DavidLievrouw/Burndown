import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { useCallback, useEffect, useRef, useState } from "react";
import { useMonthKeys } from "../../hooks/useMonthKeys";
import { addMonths } from "../../lib/months";
import { fetchOverview, type OverviewResponse } from "./api";
import { BurndownChart } from "./BurndownChart";
import { DayTable } from "./DayTable";
import { ErrorPanel } from "./ErrorPanel";
import { MonthNav } from "./MonthNav";
import { OverrideField } from "./OverrideField";
import { parseOverride } from "./override";
import { SummaryCards } from "./SummaryCards";
import { YearBlock } from "./YearBlock";

const OVERRIDE_DEBOUNCE_MS = 400;

interface Bounds {
  minimum: string;
  maximum: string;
  current: string;
}

/** The month `delta` away, or null when it is outside the allowed range or the range is not known yet. */
function stepTarget(shown: string | null, limits: Bounds | null, delta: number): string | null {
  if (!shown || !limits) {
    return null;
  }
  const target = addMonths(shown, delta);
  return target >= limits.minimum && target <= limits.maximum ? target : null;
}

function Skeleton() {
  return (
    <div aria-busy="true" aria-label="Loading overview" role="status" className="animate-pulse space-y-4">
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-3 xl:grid-cols-6">
        {Array.from({ length: 6 }, (_, index) => (
          // biome-ignore lint/suspicious/noArrayIndexKey: static placeholders
          <div key={index} className="h-20 rounded-lg bg-surface-raised" />
        ))}
      </div>
      <div className="h-96 rounded-lg bg-surface-raised" />
    </div>
  );
}

function Content({ data, dimmed }: { data: OverviewResponse; dimmed: boolean }) {
  return (
    <div className={`space-y-6 transition-opacity ${dimmed ? "opacity-60" : ""}`} aria-busy={dimmed}>
      <SummaryCards data={data} />
      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_minmax(420px,520px)]">
        <div className="xl:order-2">
          <div className="xl:sticky xl:top-4">
            <BurndownChart chart={data.chart} />
          </div>
        </div>
        <section aria-label="Daily overview" className="overflow-clip rounded-lg border border-border xl:order-1">
          <DayTable data={data} />
        </section>
      </div>
    </div>
  );
}

export function OverviewPage() {
  // null = "the server's current month" until the first response tells us which month that is.
  const [month, setMonth] = useState<string | null>(null);
  const [overrideText, setOverrideText] = useState("");
  const [appliedOverride, setAppliedOverride] = useState<string | null>(null);
  const [bounds, setBounds] = useState<Bounds | null>(null);

  useEffect(() => {
    const parsed = parseOverride(overrideText);
    if (parsed.kind === "invalid") {
      return;
    }
    const value = parsed.kind === "valid" ? parsed.value : null;
    if (value === null) {
      setAppliedOverride(null);
      return;
    }
    const timer = setTimeout(() => setAppliedOverride(value), OVERRIDE_DEBOUNCE_MS);
    return () => clearTimeout(timer);
  }, [overrideText]);

  const query = useQuery({
    queryKey: ["overview", month, appliedOverride],
    queryFn: ({ signal }) => fetchOverview(month, appliedOverride, signal),
    placeholderData: keepPreviousData,
  });
  const data = query.data;

  useEffect(() => {
    if (data) {
      setBounds({ minimum: data.minimumMonth, maximum: data.maximumMonth, current: data.currentMonth });
    }
  }, [data]);

  const shownMonth = month ?? data?.month ?? null;
  const goToMonth = useCallback((next: string) => {
    setMonth(next);
    setOverrideText("");
    setAppliedOverride(null);
  }, []);

  // Read through a ref so the key handlers keep a stable identity while the month changes.
  const stateRef = useRef({ shownMonth, bounds });
  stateRef.current = { shownMonth, bounds };
  const step = useCallback(
    (delta: number) => {
      const target = stepTarget(stateRef.current.shownMonth, stateRef.current.bounds, delta);
      if (target) {
        goToMonth(target);
      }
    },
    [goToMonth],
  );
  const onPrevious = useCallback(() => step(-1), [step]);
  const onNext = useCallback(() => step(1), [step]);
  const onCurrent = useCallback(() => {
    const { bounds: limits } = stateRef.current;
    if (limits && stateRef.current.shownMonth !== limits.current) {
      goToMonth(limits.current);
    }
  }, [goToMonth]);
  useMonthKeys({ onPrevious, onNext, onCurrent });

  return (
    <div className="mx-auto max-w-[1600px] px-6 py-6">
      <header className="mb-6 flex flex-wrap items-start justify-between gap-x-8 gap-y-3">
        <div className="flex items-center gap-6">
          <h1 className="text-2xl font-semibold">Burndown</h1>
          <MonthNav
            month={shownMonth}
            canGoPrevious={stepTarget(shownMonth, bounds, -1) !== null}
            canGoNext={stepTarget(shownMonth, bounds, 1) !== null}
            isCurrent={bounds === null || shownMonth === bounds.current}
            onPrevious={onPrevious}
            onNext={onNext}
            onCurrent={onCurrent}
          />
        </div>
        <OverrideField text={overrideText} computed={data ? data.budget.computed : null} onChange={setOverrideText} />
      </header>
      <main className="space-y-6">
        <YearBlock month={shownMonth} />
        {query.isError ? (
          <ErrorPanel error={query.error} onRetry={() => void query.refetch()} />
        ) : data ? (
          <Content data={data} dimmed={query.isPlaceholderData} />
        ) : (
          <Skeleton />
        )}
      </main>
    </div>
  );
}
