import { formatMonth } from "../../lib/format";

interface MonthNavProps {
  month: string | null;
  canGoPrevious: boolean;
  canGoNext: boolean;
  isCurrent: boolean;
  onPrevious: () => void;
  onNext: () => void;
  onCurrent: () => void;
}

const buttonClass =
  "rounded-md border border-border bg-surface-raised px-3 py-1.5 text-sm hover:border-accent disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:border-border";

export function MonthNav({ month, canGoPrevious, canGoNext, isCurrent, onPrevious, onNext, onCurrent }: MonthNavProps) {
  return (
    <nav aria-label="Month" className="flex items-center gap-2">
      <button
        type="button"
        className={buttonClass}
        onClick={onPrevious}
        disabled={!canGoPrevious}
        aria-keyshortcuts="ArrowLeft"
        aria-label="Previous month"
        title="Previous month (←)"
      >
        ‹
      </button>
      <h2 className="min-w-44 text-center text-lg font-semibold" aria-live="polite">
        {month ? formatMonth(month) : "Overview"}
      </h2>
      <button
        type="button"
        className={buttonClass}
        onClick={onNext}
        disabled={!canGoNext}
        aria-keyshortcuts="ArrowRight"
        aria-label="Next month"
        title="Next month (→)"
      >
        ›
      </button>
      <button
        type="button"
        className={buttonClass}
        onClick={onCurrent}
        disabled={isCurrent}
        aria-keyshortcuts="Home"
        title="Current month (Home)"
      >
        Current month
      </button>
    </nav>
  );
}
