import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { useDebouncedValue } from "../../hooks/useDebouncedValue";
import { formatAmountInput, parseAmount } from "./amount";
import { fetchDescriptionSuggestions, type ReferenceData, type RowType } from "./api";
import { inputClass, SelectCombobox, TextCombobox } from "./Combobox";
import { type ComboItem, containsAllWords } from "./matching";
import {
  FIELD_LABELS,
  FIELD_ORDER,
  type GridRow as GridRowModel,
  isFieldApplicable,
  type UiField,
  visibleErrors,
} from "./model";
import { formatWhen, parseWhenInput } from "./when";

/** The grid columns: #, type, when, description, amount, account, category/to, target, budget. */
export const GRID_COLUMNS = "24px 88px 144px minmax(130px,1fr) 80px 128px 128px 96px 84px";

const TYPE_ITEMS: readonly ComboItem[] = [
  { key: "expense", label: "Expense" },
  { key: "income", label: "Income" },
  { key: "transfer", label: "Transfer" },
];

export interface RowLists {
  accounts: ComboItem[];
  categories: ComboItem[];
  targets: ComboItem[];
  budgets: ComboItem[];
}

export function buildLists(reference: ReferenceData): RowLists {
  return {
    accounts: reference.accounts.map((account) => ({ key: String(account.id), label: account.name })),
    categories: reference.categories.map((name) => ({ key: name, label: name })),
    targets: reference.targetTags.map((name) => ({ key: name, label: name })),
    budgets: reference.budgets.map((budget) => ({ key: String(budget.id), label: budget.name })),
  };
}

export type RowPatch = Partial<GridRowModel>;

interface RowProps {
  row: GridRowModel;
  index: number;
  count: number;
  reference: ReferenceData;
  lists: RowLists;
  onChange: (id: number, patch: RowPatch, field: UiField) => void;
  onAction: (id: number, action: "up" | "down" | "duplicate" | "remove") => void;
}

function toId(key: string | null): number | null {
  return key === null ? null : Number(key);
}

function WhenInput(props: {
  row: GridRowModel;
  invalid: boolean;
  describedBy: string;
  disabled: boolean;
  onChange: (patch: RowPatch) => void;
}) {
  const { row, invalid, describedBy, disabled, onChange } = props;
  const [draft, setDraft] = useState<string | null>(null);

  const commit = (): boolean => {
    if (draft === null) {
      return true;
    }
    const parsed = parseWhenInput(draft, row.when);
    if (parsed.ok) {
      onChange({ when: parsed.when, whenError: null });
      setDraft(null);
      return true;
    }
    onChange({ whenError: parsed.message });
    return false;
  };

  return (
    <input
      type="text"
      aria-label="When"
      aria-invalid={invalid || undefined}
      aria-describedby={describedBy}
      autoComplete="off"
      spellCheck={false}
      data-entry-row={row.id}
      data-entry-field="when"
      disabled={disabled}
      className={inputClass(invalid)}
      value={draft ?? formatWhen(row.when)}
      onFocus={(event) => event.currentTarget.select()}
      onChange={(event) => setDraft(event.target.value)}
      onKeyDown={(event) => {
        if (event.key === "Enter" && !event.ctrlKey && !event.altKey && !commit()) {
          event.preventDefault();
        }
      }}
      onBlur={() => {
        commit();
      }}
    />
  );
}

function AmountInput(props: {
  row: GridRowModel;
  invalid: boolean;
  describedBy: string;
  disabled: boolean;
  onChange: (patch: RowPatch) => void;
}) {
  const { row, invalid, describedBy, disabled, onChange } = props;
  return (
    <input
      type="text"
      inputMode="decimal"
      aria-label="Amount"
      aria-invalid={invalid || undefined}
      aria-describedby={describedBy}
      autoComplete="off"
      spellCheck={false}
      data-entry-row={row.id}
      data-entry-field="amount"
      disabled={disabled}
      className={`${inputClass(invalid)} text-right tabular-nums`}
      value={row.amountText}
      onFocus={(event) => event.currentTarget.select()}
      onChange={(event) => onChange({ amountText: event.target.value.replace(/[^0-9.,]/g, "") })}
      onBlur={() => {
        const parsed = parseAmount(row.amountText);
        if (parsed.ok) {
          const normalized = formatAmountInput(parsed.cents);
          if (normalized !== row.amountText) {
            onChange({ amountText: normalized });
          }
        }
      }}
    />
  );
}

function DescriptionInput(props: {
  row: GridRowModel;
  invalid: boolean;
  describedBy: string;
  onChange: (patch: RowPatch) => void;
}) {
  const { row, invalid, describedBy, onChange } = props;
  const [focused, setFocused] = useState(false);
  const query = useDebouncedValue(row.description.trim(), 250);
  const { data } = useQuery({
    queryKey: ["entry", "suggest", query],
    queryFn: ({ signal }) => fetchDescriptionSuggestions(query, signal),
    enabled: focused && query.length >= 2,
    staleTime: 60_000,
    placeholderData: keepPreviousData,
  });
  const suggestions = (data ?? []).filter((text) => containsAllWords(text, row.description));

  return (
    <TextCombobox
      rowId={row.id}
      field="description"
      label="Description"
      invalid={invalid}
      describedBy={describedBy}
      value={row.description}
      suggestions={suggestions}
      onChange={(description) => onChange({ description })}
      onFocusChange={setFocused}
    />
  );
}

const NOT_APPLICABLE = (
  <span aria-hidden="true" className="block text-center text-muted/40">
    —
  </span>
);

function describeStatus(errors: Partial<Record<UiField, string>>) {
  const entries = FIELD_ORDER.flatMap((field) => {
    const message = errors[field];
    return message ? [`${FIELD_LABELS[field]}: ${message}`] : [];
  });
  return entries;
}

export function GridRow({ row, index, count, reference, lists, onChange, onAction }: RowProps) {
  const locked = row.status !== "draft";
  const errors = visibleErrors(row, reference);
  const statusId = `entry-row-${row.id}-status`;
  const invalid = (field: UiField) => errors[field] !== undefined;
  const patch = (field: UiField) => (change: RowPatch) => onChange(row.id, change, field);
  const issues = describeStatus(errors);
  const applicable = (field: UiField) => isFieldApplicable(row.type, field);
  const describedBy = statusId;

  let statusText = "";
  let statusTone = "text-muted";
  if (row.status === "created") {
    statusText = `✓ Created in Firefly III${row.createdId === null ? "" : ` (#${row.createdId})`}`;
    statusTone = "text-ok";
  } else if (issues.length > 0) {
    statusText = issues.length > 1 ? `${issues[0]} (+${issues.length - 1} more)` : (issues[0] ?? "");
    statusTone = "text-bad";
  }
  const noticeText = row.notice?.text ?? "";
  const noticeTone =
    row.notice?.tone === "bad" ? "text-bad" : row.notice?.tone === "warn" ? "text-amber-300" : "text-muted";
  const full = [statusText, ...issues.slice(1), noticeText].filter(Boolean).join(" · ");

  const typeChange = (key: string | null) => {
    if (key === null) {
      return;
    }
    onChange(row.id, { type: key as RowType }, "type");
  };

  const actionClass =
    "flex h-5 w-5 items-center justify-center rounded text-xs text-muted hover:bg-surface-raised hover:text-text disabled:opacity-30";

  return (
    <div
      role="rowgroup"
      data-entry-row-id={row.id}
      data-status={row.status}
      className={`border-b border-border/50 py-1 ${row.status === "created" ? "bg-ok/5" : ""} ${row.status === "uncertain" ? "bg-amber-400/5" : ""}`}
    >
      <div role="row" className="grid items-center gap-1.5" style={{ gridTemplateColumns: GRID_COLUMNS }}>
        <div role="gridcell" className="text-center text-xs tabular-nums text-muted">
          {index + 1}
        </div>
        <div role="gridcell">
          <SelectCombobox
            rowId={row.id}
            field="type"
            label="Type"
            items={TYPE_ITEMS}
            value={row.type}
            disabled={locked}
            invalid={invalid("type")}
            describedBy={describedBy}
            onSelect={typeChange}
          />
        </div>
        <div role="gridcell">
          <WhenInput
            row={row}
            invalid={invalid("when")}
            describedBy={describedBy}
            disabled={locked}
            onChange={patch("when")}
          />
        </div>
        <div role="gridcell">
          {locked ? (
            <input
              aria-label="Description"
              disabled
              value={row.description}
              readOnly
              className={inputClass(false)}
              data-entry-row={row.id}
              data-entry-field="description"
            />
          ) : (
            <DescriptionInput
              row={row}
              invalid={invalid("description")}
              describedBy={describedBy}
              onChange={patch("description")}
            />
          )}
        </div>
        <div role="gridcell">
          <AmountInput
            row={row}
            invalid={invalid("amount")}
            describedBy={describedBy}
            disabled={locked}
            onChange={patch("amount")}
          />
        </div>
        <div role="gridcell">
          <SelectCombobox
            rowId={row.id}
            field="account"
            label={row.type === "transfer" ? "From account" : "Account"}
            items={lists.accounts}
            value={row.accountId === null ? null : String(row.accountId)}
            disabled={locked}
            invalid={invalid("account")}
            describedBy={describedBy}
            onSelect={(key) => onChange(row.id, { accountId: toId(key) }, "account")}
          />
        </div>
        <div role="gridcell">
          {!applicable("second") ? (
            NOT_APPLICABLE
          ) : row.type === "transfer" ? (
            <SelectCombobox
              rowId={row.id}
              field="second"
              label="To account"
              items={lists.accounts}
              value={row.toAccountId === null ? null : String(row.toAccountId)}
              disabled={locked}
              invalid={invalid("second")}
              describedBy={describedBy}
              onSelect={(key) => onChange(row.id, { toAccountId: toId(key) }, "second")}
            />
          ) : (
            <SelectCombobox
              rowId={row.id}
              field="second"
              label="Category"
              items={lists.categories}
              value={row.category}
              disabled={locked}
              invalid={invalid("second")}
              describedBy={describedBy}
              onSelect={(key) => onChange(row.id, { category: key }, "second")}
            />
          )}
        </div>
        <div role="gridcell">
          {applicable("target") ? (
            <SelectCombobox
              rowId={row.id}
              field="target"
              label="Target"
              items={lists.targets}
              value={row.tag}
              disabled={locked}
              invalid={invalid("target")}
              describedBy={describedBy}
              onSelect={(key) => onChange(row.id, { tag: key }, "target")}
            />
          ) : (
            NOT_APPLICABLE
          )}
        </div>
        <div role="gridcell">
          {applicable("budget") ? (
            <SelectCombobox
              rowId={row.id}
              field="budget"
              label="Budget"
              items={lists.budgets}
              value={row.budgetId === null ? null : String(row.budgetId)}
              allowEmpty
              disabled={locked}
              invalid={invalid("budget")}
              describedBy={describedBy}
              onSelect={(key) => onChange(row.id, { budgetId: toId(key) }, "budget")}
            />
          ) : (
            NOT_APPLICABLE
          )}
        </div>
      </div>
      <div role="row" className="mt-0.5 flex h-5 items-center justify-between gap-3 pl-[30px] pr-1">
        <div role="gridcell" id={statusId} title={full} className="min-w-0 flex-1 truncate text-xs">
          <span className={statusTone}>{statusText}</span>
          {noticeText ? <span className={`${statusText ? " ml-2" : ""} ${noticeTone}`}>{noticeText}</span> : null}
        </div>
        <div role="gridcell" className="flex shrink-0 items-center gap-0.5">
          <button
            type="button"
            tabIndex={-1}
            className={actionClass}
            aria-label={`Move row ${index + 1} up`}
            title="Move up (Alt+↑)"
            disabled={index === 0}
            onClick={() => onAction(row.id, "up")}
          >
            ↑
          </button>
          <button
            type="button"
            tabIndex={-1}
            className={actionClass}
            aria-label={`Move row ${index + 1} down`}
            title="Move down (Alt+↓)"
            disabled={index === count - 1}
            onClick={() => onAction(row.id, "down")}
          >
            ↓
          </button>
          <button
            type="button"
            tabIndex={-1}
            className={actionClass}
            aria-label={`Duplicate row ${index + 1}`}
            title="Duplicate (Ctrl+D)"
            onClick={() => onAction(row.id, "duplicate")}
          >
            ⧉
          </button>
          <button
            type="button"
            tabIndex={-1}
            className={`${actionClass} hover:text-bad`}
            aria-label={`Delete row ${index + 1}`}
            title="Delete (Alt+Delete)"
            onClick={() => onAction(row.id, "remove")}
          >
            ✕
          </button>
        </div>
      </div>
    </div>
  );
}
