import { useId } from "react";
import { formatNumber } from "../../lib/format";
import { parseOverride } from "./override";

interface OverrideFieldProps {
  text: string;
  computed: number | null;
  onChange: (text: string) => void;
}

export function OverrideField({ text, computed, onChange }: Readonly<OverrideFieldProps>) {
  const id = useId();
  const errorId = `${id}-error`;
  const parsed = parseOverride(text);
  const message = parsed.kind === "invalid" ? parsed.message : null;

  return (
    <div className="flex flex-col gap-1">
      <div className="flex items-center gap-2">
        <label htmlFor={id} className="text-sm text-muted">
          Budget override
        </label>
        <div className="relative">
          <span
            aria-hidden="true"
            className="pointer-events-none absolute inset-y-0 left-2 flex items-center text-muted"
          >
            €
          </span>
          <input
            id={id}
            type="text"
            inputMode="decimal"
            autoComplete="off"
            spellCheck={false}
            value={text}
            placeholder={computed === null ? "" : formatNumber(computed)}
            aria-invalid={message ? true : undefined}
            aria-describedby={message ? errorId : undefined}
            onChange={(event) => onChange(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === "Escape" && text !== "") {
                event.preventDefault();
                onChange("");
              }
            }}
            className="w-36 rounded-md border border-border bg-surface py-1.5 pr-2 pl-6 text-right text-sm tabular-nums placeholder:text-muted aria-[invalid=true]:border-bad"
          />
        </div>
        <button
          type="button"
          onClick={() => onChange("")}
          disabled={text === ""}
          className="rounded-md px-2 py-1.5 text-sm text-muted hover:text-text disabled:invisible"
        >
          Reset
        </button>
      </div>
      <p id={errorId} className="min-h-4 text-xs text-bad" role={message ? "alert" : undefined}>
        {message}
      </p>
    </div>
  );
}
