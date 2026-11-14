import { type KeyboardEvent, useEffect, useId, useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { type ComboItem, filterItems, normalize } from "./matching";
import type { UiField } from "./model";

interface BaseProps {
  rowId: number;
  field: UiField;
  label: string;
  invalid?: boolean;
  describedBy?: string;
  placeholder?: string;
  disabled?: boolean;
  onFocusChange?: (focused: boolean) => void;
}

const INPUT_CLASS =
  "h-8 w-full min-w-0 rounded border bg-surface px-2 text-sm text-text outline-none placeholder:text-muted/60 focus:border-accent focus:ring-1 focus:ring-accent disabled:opacity-60";

export function inputClass(invalid: boolean | undefined): string {
  return `${INPUT_CLASS} ${invalid ? "border-bad" : "border-border"}`;
}

interface PopupProps {
  anchor: HTMLElement | null;
  listboxId: string;
  options: readonly ComboItem[];
  highlight: number;
  onPick: (index: number) => void;
  hint?: string;
}

/** The suggestion list. It is fixed-positioned under the input so that it never changes the grid's layout. */
function Popup({ anchor, listboxId, options, highlight, onPick, hint }: PopupProps) {
  const [box, setBox] = useState<{ left: number; top: number; width: number } | null>(null);
  const listRef = useRef<HTMLDivElement>(null);

  useLayoutEffect(() => {
    if (!anchor) {
      return;
    }
    const update = () => {
      const rect = anchor.getBoundingClientRect();
      setBox({ left: rect.left, top: rect.bottom + 2, width: rect.width });
    };
    update();
    window.addEventListener("scroll", update, true);
    window.addEventListener("resize", update);
    return () => {
      window.removeEventListener("scroll", update, true);
      window.removeEventListener("resize", update);
    };
  }, [anchor]);

  // biome-ignore lint/correctness/useExhaustiveDependencies: scroll the highlighted option into view when it changes
  useEffect(() => {
    listRef.current?.querySelector('[aria-selected="true"]')?.scrollIntoView({ block: "nearest" });
  }, [highlight]);

  if (!box) {
    return null;
  }
  return createPortal(
    <div
      className="fixed z-50 overflow-hidden rounded-md border border-border bg-surface-raised shadow-xl"
      style={{ left: box.left, top: box.top, minWidth: Math.max(box.width, 200) }}
    >
      <div
        ref={listRef}
        id={listboxId}
        role="listbox"
        aria-label="Suggestions"
        className="max-h-56 overflow-y-auto py-1"
      >
        {options.map((option, index) => (
          <div
            key={option.key}
            id={`${listboxId}-${index}`}
            role="option"
            aria-selected={index === highlight}
            tabIndex={-1}
            onMouseDown={(event) => event.preventDefault()}
            onClick={() => onPick(index)}
            onKeyDown={() => undefined}
            className={`cursor-pointer px-2 py-1 text-sm ${index === highlight ? "bg-accent/25 text-text" : "text-text/90 hover:bg-surface"}`}
          >
            {option.label}
          </div>
        ))}
      </div>
      {hint ? <div className="border-t border-border px-2 py-1 text-xs text-muted">{hint}</div> : null}
    </div>,
    document.body,
  );
}

function isModified(event: KeyboardEvent): boolean {
  return event.altKey || event.ctrlKey || event.metaKey;
}

interface SelectProps extends BaseProps {
  items: readonly ComboItem[];
  value: string | null;
  allowEmpty?: boolean;
  onSelect: (key: string | null) => void;
}

/**
 * A combobox over a fixed list. Typing filters it and pre-highlights the first match; Enter or Tab accepts it, and
 * leaving the field without a match restores the previous value.
 */
export function SelectCombobox(props: SelectProps) {
  const {
    items,
    value,
    allowEmpty,
    onSelect,
    rowId,
    field,
    label,
    invalid,
    describedBy,
    placeholder,
    disabled,
    onFocusChange,
  } = props;
  const listboxId = useId();
  const inputRef = useRef<HTMLInputElement>(null);
  const [draft, setDraft] = useState<string | null>(null);
  const [open, setOpen] = useState(false);
  const [highlight, setHighlight] = useState(0);
  // Whether the user moved through the list with the arrow keys since the text last changed.
  const [navigated, setNavigated] = useState(false);
  // Set when an option was just accepted, so the blur that follows does not resolve the stale typed text again.
  const acceptedRef = useRef(false);
  const selectedLabel = items.find((item) => item.key === value)?.label ?? "";
  const options = open ? filterItems(items, draft ?? "") : [];
  const showing = open && options.length > 0;

  const accept = (index: number) => {
    const option = options[index];
    if (option) {
      onSelect(option.key);
    }
    acceptedRef.current = true;
    setDraft(null);
    setOpen(false);
    setNavigated(false);
  };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (isModified(event)) {
      return;
    }
    switch (event.key) {
      case "ArrowDown":
      case "ArrowUp": {
        event.preventDefault();
        setNavigated(true);
        if (!open) {
          setOpen(true);
          setHighlight(0);
          return;
        }
        const count = options.length;
        if (count > 0) {
          setHighlight((current) => (current + (event.key === "ArrowDown" ? 1 : -1) + count) % count);
        }
        return;
      }
      case "Enter": {
        if (showing && navigated) {
          // Accepts the option the user moved to, then lets the grid move on.
          accept(highlight);
          return;
        }
        if (draft !== null && draft.trim() === "") {
          if (allowEmpty) {
            onSelect(null);
            setDraft(null);
            setOpen(false);
          } else {
            event.preventDefault();
          }
          return;
        }
        if (draft !== null) {
          if (showing) {
            accept(highlight);
          } else {
            event.preventDefault();
          }
        }
        return;
      }
      case "Tab": {
        // An emptied box only accepts a highlighted option the user actively moved to; otherwise it is left as is.
        if (showing && (navigated || (draft !== null && draft.trim() !== ""))) {
          accept(highlight);
        }
        return;
      }
      case "Escape": {
        if (open || draft !== null) {
          event.preventDefault();
          event.stopPropagation();
          setDraft(null);
          setOpen(false);
          setNavigated(false);
        }
        return;
      }
    }
  };

  const onBlur = () => {
    const accepted = acceptedRef.current;
    acceptedRef.current = false;
    if (draft !== null && !accepted) {
      const typed = normalize(draft.trim());
      if (typed === "") {
        if (allowEmpty) {
          onSelect(null);
        }
      } else {
        const exact = items.find((item) => normalize(item.label) === typed);
        if (exact) {
          onSelect(exact.key);
        }
      }
    }
    setDraft(null);
    setOpen(false);
    setNavigated(false);
    onFocusChange?.(false);
  };

  return (
    <>
      <input
        ref={inputRef}
        type="text"
        role="combobox"
        aria-label={label}
        aria-expanded={showing}
        aria-controls={showing ? listboxId : undefined}
        aria-activedescendant={showing ? `${listboxId}-${highlight}` : undefined}
        aria-autocomplete="list"
        aria-invalid={invalid || undefined}
        aria-describedby={describedBy}
        autoComplete="off"
        spellCheck={false}
        data-entry-row={rowId}
        data-entry-field={field}
        placeholder={placeholder}
        disabled={disabled}
        className={inputClass(invalid)}
        value={draft ?? selectedLabel}
        onFocus={(event) => {
          acceptedRef.current = false;
          event.currentTarget.select();
          onFocusChange?.(true);
        }}
        onChange={(event) => {
          acceptedRef.current = false;
          setDraft(event.target.value);
          setOpen(true);
          setHighlight(0);
          setNavigated(false);
        }}
        onKeyDown={onKeyDown}
        onBlur={onBlur}
      />
      {showing ? (
        <Popup
          anchor={inputRef.current}
          listboxId={listboxId}
          options={options}
          highlight={highlight}
          onPick={accept}
        />
      ) : null}
    </>
  );
}

interface TextProps extends BaseProps {
  value: string;
  suggestions: readonly string[];
  onChange: (text: string) => void;
}

/**
 * A free-text field with suggestions. A suggestion that starts with the typed text is pre-highlighted; Enter or Tab
 * accepts it, Escape closes the list and keeps what was typed.
 */
export function TextCombobox(props: TextProps) {
  const {
    value,
    suggestions,
    onChange,
    rowId,
    field,
    label,
    invalid,
    describedBy,
    placeholder,
    disabled,
    onFocusChange,
  } = props;
  const listboxId = useId();
  const inputRef = useRef<HTMLInputElement>(null);
  const [focused, setFocused] = useState(false);
  const [dismissed, setDismissed] = useState(false);
  const [highlight, setHighlight] = useState(-1);

  const typed = normalize(value.trim());
  const options: ComboItem[] = suggestions
    .filter((text) => normalize(text) !== typed)
    .map((text) => ({ key: text, label: text }));
  const ranked = [
    ...options.filter((option) => normalize(option.label).startsWith(typed)),
    ...options.filter((option) => !normalize(option.label).startsWith(typed)),
  ];
  const showing = focused && !dismissed && typed.length >= 2 && ranked.length > 0;
  // Without an explicit choice, only a suggestion that starts with the typed text is pre-highlighted.
  const defaultHighlight = ranked[0] && normalize(ranked[0].label).startsWith(typed) ? 0 : -1;
  const current = highlight >= 0 && highlight < ranked.length ? highlight : defaultHighlight;
  const active = showing ? current : -1;

  const accept = (index: number) => {
    const option = ranked[index];
    if (option) {
      onChange(option.label);
    }
    setDismissed(true);
    setHighlight(-1);
  };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (isModified(event)) {
      return;
    }
    switch (event.key) {
      case "ArrowDown":
      case "ArrowUp": {
        if (!showing) {
          return;
        }
        event.preventDefault();
        const count = ranked.length;
        if (event.key === "ArrowDown") {
          setHighlight(current < 0 ? 0 : (current + 1) % count);
        } else {
          setHighlight(current <= 0 ? count - 1 : current - 1);
        }
        return;
      }
      case "Enter":
      case "Tab": {
        if (active >= 0) {
          accept(active);
        }
        return;
      }
      case "Escape": {
        if (showing) {
          event.preventDefault();
          event.stopPropagation();
          setDismissed(true);
        }
        return;
      }
    }
  };

  return (
    <>
      <input
        ref={inputRef}
        type="text"
        role="combobox"
        aria-label={label}
        aria-expanded={showing}
        aria-controls={showing ? listboxId : undefined}
        aria-activedescendant={active >= 0 ? `${listboxId}-${active}` : undefined}
        aria-autocomplete="list"
        aria-invalid={invalid || undefined}
        aria-describedby={describedBy}
        autoComplete="off"
        spellCheck={false}
        data-entry-row={rowId}
        data-entry-field={field}
        placeholder={placeholder}
        disabled={disabled}
        className={inputClass(invalid)}
        value={value}
        onFocus={() => {
          setFocused(true);
          setDismissed(false);
          setHighlight(-1);
          onFocusChange?.(true);
        }}
        onBlur={() => {
          setFocused(false);
          onFocusChange?.(false);
        }}
        onChange={(event) => {
          onChange(event.target.value);
          setDismissed(false);
          setHighlight(-1);
        }}
        onKeyDown={onKeyDown}
      />
      {showing ? (
        <Popup
          anchor={inputRef.current}
          listboxId={listboxId}
          options={ranked}
          highlight={active}
          onPick={accept}
          hint="Enter or Tab accepts · Esc keeps what you typed"
        />
      ) : null}
    </>
  );
}
