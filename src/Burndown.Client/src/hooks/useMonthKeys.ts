import { useEffect } from "react";

export interface MonthKeyHandlers {
  onPrevious: () => void;
  onNext: () => void;
  onCurrent: () => void;
}

const TEXT_ENTRY_TAGS = new Set(["INPUT", "TEXTAREA", "SELECT"]);

function isTextEntry(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) {
    return false;
  }
  return TEXT_ENTRY_TAGS.has(target.tagName) || target.isContentEditable;
}

/**
 * Month navigation from the keyboard: ArrowLeft = previous, ArrowRight = next, Home = current month.
 * Keys typed into a text field, combined with a modifier, or already handled elsewhere are left alone.
 */
export function useMonthKeys({ onPrevious, onNext, onCurrent }: MonthKeyHandlers): void {
  useEffect(() => {
    function handle(event: KeyboardEvent) {
      if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) {
        return;
      }
      if (isTextEntry(event.target)) {
        return;
      }
      const action =
        event.key === "ArrowLeft"
          ? onPrevious
          : event.key === "ArrowRight"
            ? onNext
            : event.key === "Home"
              ? onCurrent
              : null;
      if (action) {
        event.preventDefault();
        action();
      }
    }

    window.addEventListener("keydown", handle);
    return () => window.removeEventListener("keydown", handle);
  }, [onPrevious, onNext, onCurrent]);
}
