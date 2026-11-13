import type { RowResult, SubmitResponse } from "./api";
import { createStickyRow, type GridRow, isBlank, isIgnoredRow, type UiField } from "./model";
import { addMinutes, formatWhen } from "./when";

export interface GridState {
  rows: GridRow[];
  nextId: number;
}

export type GridAction =
  | { kind: "update"; id: number; patch: Partial<GridRow>; field: UiField }
  | { kind: "append" }
  | { kind: "duplicate"; id: number }
  | { kind: "remove"; id: number }
  | { kind: "move"; id: number; direction: -1 | 1 }
  | { kind: "applyResults"; sentIds: number[]; response: SubmitResponse }
  | { kind: "showErrors" }
  | { kind: "clearCreated" }
  | { kind: "resetUncertain" };

export function initialGrid(first: GridRow): GridState {
  return { rows: [first], nextId: first.id + 1 };
}

const SERVER_FIELDS: Record<string, UiField | "row"> = {
  type: "type",
  dateTime: "when",
  description: "description",
  amount: "amount",
  accountId: "account",
  fromAccountId: "account",
  toAccountId: "second",
  category: "second",
  tag: "target",
  budgetId: "budget",
  row: "row",
};

function mapErrors(result: RowResult): { fields: Partial<Record<UiField, string>>; general: string | null } {
  const fields: Partial<Record<UiField, string>> = {};
  const general: string[] = [];
  for (const [key, messages] of Object.entries(result.errors ?? {})) {
    const text = messages.join(" ");
    const field = SERVER_FIELDS[key] ?? "row";
    if (field === "row") {
      general.push(text);
    } else {
      fields[field] = fields[field] ? `${fields[field]} ${text}` : text;
    }
  }
  return { fields, general: general.length > 0 ? general.join(" ") : null };
}

function applyResult(row: GridRow, result: RowResult): GridRow {
  switch (result.status) {
    case "created": {
      const when = result.dateTime ?? row.when;
      return {
        ...row,
        when,
        status: "created",
        createdId: result.id,
        serverErrors: {},
        showErrors: false,
        notice:
          result.bumped && result.dateTime
            ? { tone: "muted", text: `Time moved to ${formatWhen(when).slice(11)} to keep the entry order.` }
            : null,
      };
    }
    case "invalid":
    case "rejected": {
      const { fields, general } = mapErrors(result);
      const hasFields = Object.keys(fields).length > 0;
      return {
        ...row,
        serverErrors: fields,
        showErrors: true,
        notice:
          !hasFields || general ? { tone: "bad", text: general ?? result.message ?? "The row was rejected." } : null,
      };
    }
    case "failed":
      return {
        ...row,
        notice: { tone: "bad", text: result.message ?? "The row could not be sent. It can be submitted again." },
      };
    case "uncertain":
      return {
        ...row,
        status: "uncertain",
        notice: {
          tone: "warn",
          text: `${result.message ?? "The result is unknown."} Check Firefly III before sending it again.`,
        },
      };
    case "notAttempted":
      return { ...row, notice: { tone: "muted", text: "Not sent: an earlier row stopped the batch." } };
  }
}

/** Keeps blank rows after the previous row in time, so the next batch continues where the last one ended. */
function continueTimes(rows: GridRow[]): GridRow[] {
  return rows.map((row, index) => {
    const previous = rows[index - 1];
    if (previous && row.status === "draft" && isBlank(row) && row.whenError === null && row.when <= previous.when) {
      return { ...row, when: addMinutes(previous.when, 1) };
    }
    return row;
  });
}

function ensureRow(state: GridState, rows: GridRow[], template: GridRow | undefined): GridState {
  if (rows.length > 0) {
    return { rows, nextId: state.nextId };
  }
  const base = template ? { ...template, when: addMinutes(template.when, -1) } : state.rows[state.rows.length - 1];
  if (!base) {
    return state;
  }
  const fresh = createStickyRow(base, state.nextId);
  return { rows: [fresh], nextId: state.nextId + 1 };
}

export function gridReducer(state: GridState, action: GridAction): GridState {
  switch (action.kind) {
    case "update": {
      return {
        ...state,
        rows: state.rows.map((row) => {
          if (row.id !== action.id || row.status !== "draft") {
            return row;
          }
          const serverErrors = { ...row.serverErrors };
          if (action.patch.type !== undefined && action.patch.type !== row.type) {
            for (const key of Object.keys(serverErrors)) {
              delete serverErrors[key as UiField];
            }
          } else {
            delete serverErrors[action.field];
          }
          return { ...row, ...action.patch, serverErrors, notice: null };
        }),
      };
    }
    case "append": {
      const last = state.rows[state.rows.length - 1];
      if (!last) {
        return state;
      }
      return {
        rows: [...state.rows, createStickyRow(last, state.nextId)],
        nextId: state.nextId + 1,
      };
    }
    case "duplicate": {
      const index = state.rows.findIndex((row) => row.id === action.id);
      const source = state.rows[index];
      if (!source) {
        return state;
      }
      const copy: GridRow = {
        ...createStickyRow(source, state.nextId),
        description: source.description,
        amountText: source.amountText,
      };
      return {
        rows: [...state.rows.slice(0, index + 1), copy, ...state.rows.slice(index + 1)],
        nextId: state.nextId + 1,
      };
    }
    case "remove": {
      const index = state.rows.findIndex((row) => row.id === action.id);
      if (index < 0) {
        return state;
      }
      const removed = state.rows[index];
      return ensureRow(
        state,
        state.rows.filter((row) => row.id !== action.id),
        removed,
      );
    }
    case "move": {
      const index = state.rows.findIndex((row) => row.id === action.id);
      const target = index + action.direction;
      if (index < 0 || target < 0 || target >= state.rows.length) {
        return state;
      }
      const rows = [...state.rows];
      const [moved] = rows.splice(index, 1);
      if (!moved) {
        return state;
      }
      rows.splice(target, 0, moved);
      return { ...state, rows };
    }
    case "applyResults": {
      const byId = new Map<number, RowResult>();
      action.sentIds.forEach((id, position) => {
        const result = action.response.results[position];
        if (result) {
          byId.set(id, result);
        }
      });
      const rows = state.rows.map((row) => {
        const result = byId.get(row.id);
        return result ? applyResult(row, result) : row;
      });
      return { ...state, rows: continueTimes(rows) };
    }
    case "showErrors":
      return {
        ...state,
        rows: state.rows.map((row, index) =>
          row.status === "draft" && !isIgnoredRow(state.rows, index) ? { ...row, showErrors: true } : row,
        ),
      };
    case "clearCreated":
      return ensureRow(
        state,
        state.rows.filter((row) => row.status !== "created"),
        undefined,
      );
    case "resetUncertain":
      return {
        ...state,
        rows: state.rows.map((row) =>
          row.status === "uncertain" ? { ...row, status: "draft", notice: null, createdId: null } : row,
        ),
      };
  }
}
