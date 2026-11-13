import { centsToAmount, parseAmount } from "./amount";
import type { AccountItem, ReferenceData, RowType, SubmitRow } from "./api";
import { addMinutes, formatWhen, whenDate, whenFromServerNow } from "./when";

/** The columns of the grid; `row` is the row-level slot for errors that belong to no single field. */
export type UiField = "type" | "when" | "description" | "amount" | "account" | "second" | "target" | "budget";

export const FIELD_ORDER: readonly UiField[] = [
  "type",
  "when",
  "description",
  "amount",
  "account",
  "second",
  "target",
  "budget",
];

export const FIELD_LABELS: Record<UiField, string> = {
  type: "Type",
  when: "When",
  description: "Description",
  amount: "Amount",
  account: "Account",
  second: "Category / to",
  target: "Target",
  budget: "Budget",
};

export type RowState = "draft" | "created" | "uncertain";

export interface Notice {
  tone: "bad" | "warn" | "muted";
  text: string;
}

export interface GridRow {
  id: number;
  type: RowType;
  /** Local date-time `yyyy-MM-ddTHH:mm` in the configured zone. */
  when: string;
  /** Set while the typed date-time cannot be read; the field keeps the typed text until it is fixed. */
  whenError: string | null;
  description: string;
  amountText: string;
  /** Expense source, income destination, transfer source. */
  accountId: number | null;
  /** Transfer destination. */
  toAccountId: number | null;
  /** Expense category. */
  category: string | null;
  /** Expense target tag. */
  tag: string | null;
  budgetId: number | null;
  status: RowState;
  createdId: number | null;
  /** Messages the server gave for this row, by column. Cleared per field when that field is edited. */
  serverErrors: Partial<Record<UiField, string>>;
  notice: Notice | null;
  /** Whether client-side validation messages are shown (after a submit attempt). */
  showErrors: boolean;
}

export function createFirstRow(reference: ReferenceData): GridRow {
  return {
    id: 1,
    type: "expense",
    when: whenFromServerNow(reference.now),
    whenError: null,
    description: "",
    amountText: "",
    accountId: null,
    toAccountId: null,
    category: null,
    tag: null,
    budgetId: null,
    status: "draft",
    createdId: null,
    serverErrors: {},
    notice: null,
    showErrors: false,
  };
}

/** A new row that carries over everything the previous row had that is likely to repeat. */
export function createStickyRow(previous: GridRow, id: number): GridRow {
  return {
    ...previous,
    id,
    when: addMinutes(previous.when, 1),
    whenError: null,
    description: "",
    amountText: "",
    status: "draft",
    createdId: null,
    serverErrors: {},
    notice: null,
    showErrors: false,
  };
}

/** A row without a description and an amount: the empty row at the end of the list. */
export function isBlank(row: GridRow): boolean {
  return row.description.trim() === "" && row.amountText.trim() === "";
}

/**
 * Submit ignores only the empty placeholder row at the very end of the list. Any other row without a description is
 * invalid, so a forgotten row can never be skipped silently.
 */
export function isIgnoredRow(rows: readonly GridRow[], index: number): boolean {
  const row = rows[index];
  return row !== undefined && index === rows.length - 1 && row.status === "draft" && isBlank(row);
}

/** The draft rows that submit takes into account (and validates). */
export function pendingRows(rows: readonly GridRow[]): GridRow[] {
  return rows.filter((row, index) => row.status === "draft" && !isIgnoredRow(rows, index));
}

/** Whether a field takes part in a row of this type; the other cells are shown as "—" and skipped by Tab. */
export function isFieldApplicable(type: RowType, field: UiField): boolean {
  switch (field) {
    case "second":
      return type !== "income";
    case "target":
    case "budget":
      return type === "expense";
    default:
      return true;
  }
}

export type RowErrors = Partial<Record<UiField, string>>;

const MAX_DESCRIPTION = 1000;

/** Mirrors the server's rules as an input affordance; the server stays authoritative. */
export function validateRow(row: GridRow, reference: ReferenceData): RowErrors {
  const errors: RowErrors = {};

  if (row.whenError !== null) {
    errors.when = row.whenError;
  } else {
    const date = whenDate(row.when);
    if (date < reference.minDate || date > reference.maxDate) {
      errors.when = `The date must be between ${formatWhen(`${reference.minDate}T00:00`).slice(0, 10)} and ${formatWhen(`${reference.maxDate}T00:00`).slice(0, 10)}.`;
    }
  }

  const description = row.description.trim();
  if (description === "") {
    errors.description = "Enter a description.";
  } else if (description.length > MAX_DESCRIPTION) {
    errors.description = `A description can have at most ${MAX_DESCRIPTION} characters.`;
  }

  const amount = parseAmount(row.amountText);
  if (!amount.ok) {
    errors.amount = amount.message;
  }

  const knownAccount = (id: number | null) => id !== null && reference.accounts.some((account) => account.id === id);
  if (!knownAccount(row.accountId)) {
    errors.account = row.type === "transfer" ? "Choose the account to transfer from." : "Choose an account.";
  }

  if (row.type === "expense") {
    if (row.category === null) {
      errors.second = "Choose a category.";
    }
    if (row.tag === null) {
      errors.target = "Choose a target.";
    }
    if (row.budgetId !== null && !reference.budgets.some((budget) => budget.id === row.budgetId)) {
      errors.budget = "Choose a budget from the list.";
    }
  } else if (row.type === "transfer") {
    if (!knownAccount(row.toAccountId)) {
      errors.second = "Choose the account to transfer to.";
    } else if (row.toAccountId === row.accountId) {
      errors.second = "The two accounts must differ.";
    }
  }

  return errors;
}

/** Builds the payload of a valid row. Throws when the row is not valid: validate first. */
export function toSubmitRow(row: GridRow): SubmitRow {
  const amount = parseAmount(row.amountText);
  if (!amount.ok) {
    throw new Error("Cannot submit a row with an invalid amount.");
  }
  return {
    type: row.type,
    dateTime: row.when,
    description: row.description.trim(),
    amount: centsToAmount(amount.cents),
    accountId: row.type === "transfer" ? null : row.accountId,
    fromAccountId: row.type === "transfer" ? row.accountId : null,
    toAccountId: row.type === "transfer" ? row.toAccountId : null,
    category: row.type === "expense" ? row.category : null,
    tag: row.type === "expense" ? row.tag : null,
    budgetId: row.type === "expense" ? row.budgetId : null,
  };
}

/** The validation messages that apply to the row right now, by column. */
export function visibleErrors(row: GridRow, reference: ReferenceData): RowErrors {
  if (row.status !== "draft") {
    return {};
  }
  let client: RowErrors = {};
  if (row.showErrors) {
    client = validateRow(row, reference);
  } else {
    if (row.whenError !== null) {
      client.when = row.whenError;
    }
    // Nonsense in the amount is flagged while typing; a missing amount only after a submit attempt.
    const amount = parseAmount(row.amountText);
    if (!amount.ok && row.amountText.trim() !== "") {
      client.amount = amount.message;
    }
  }
  return { ...client, ...row.serverErrors };
}

export function accountName(accounts: readonly AccountItem[], id: number | null): string {
  return accounts.find((account) => account.id === id)?.name ?? "";
}
