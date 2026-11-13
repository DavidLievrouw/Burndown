import { describe, expect, it } from "vitest";
import type { ReferenceData, RowResult, SubmitResponse } from "./api";
import { gridReducer, initialGrid } from "./gridReducer";
import { createFirstRow, type GridRow, isFieldApplicable, toSubmitRow, validateRow, visibleErrors } from "./model";

const reference: ReferenceData = {
  now: "2026-03-05T14:30:12.000+01:00",
  today: "2026-03-05",
  timeZone: "Europe/Brussels",
  minDate: "2023-12-01",
  maxDate: "2026-03-06",
  maxBatchSize: 100,
  accounts: [
    { id: 1, name: "Checking" },
    { id: 2, name: "Savings" },
  ],
  categories: ["Leven", "Nodig"],
  targetTags: ["David", "Tina"],
  budgets: [{ id: 13, name: "David" }],
};

function filled(row: GridRow, patch: Partial<GridRow> = {}): GridRow {
  return {
    ...row,
    description: "Bakery",
    amountText: "12,50",
    accountId: 1,
    category: "Leven",
    tag: "David",
    ...patch,
  };
}

function setup(): { rows: GridRow[]; state: ReturnType<typeof initialGrid> } {
  const state = initialGrid(createFirstRow(reference));
  return { rows: state.rows, state };
}

function result(index: number, patch: Partial<RowResult>): RowResult {
  return {
    index,
    status: "created",
    id: 100 + index,
    dateTime: null,
    bumped: false,
    code: null,
    message: null,
    errors: null,
    ...patch,
  };
}

function response(results: RowResult[]): SubmitResponse {
  return {
    results,
    summary: { created: 0, invalid: 0, rejected: 0, failed: 0, uncertain: 0, notAttempted: 0 },
    stopped: null,
  };
}

describe("createFirstRow", () => {
  it("starts as an expense at the server's local time", () => {
    const row = createFirstRow(reference);
    expect(row.type).toBe("expense");
    expect(row.when).toBe("2026-03-05T14:30");
    expect(row.accountId).toBeNull();
  });
});

describe("validateRow", () => {
  it("reports every missing field of an empty expense", () => {
    const errors = validateRow(createFirstRow(reference), reference);
    expect(Object.keys(errors).sort()).toEqual(["account", "amount", "description", "second", "target"]);
  });

  it("accepts a complete expense", () => {
    expect(validateRow(filled(createFirstRow(reference)), reference)).toEqual({});
  });

  it("does not need a category, target or budget for an income", () => {
    const row = filled(createFirstRow(reference), { type: "income", category: null, tag: null });
    expect(validateRow(row, reference)).toEqual({});
  });

  it("requires two different accounts for a transfer", () => {
    const row = filled(createFirstRow(reference), { type: "transfer", category: null, tag: null });
    expect(validateRow(row, reference).second).toMatch(/transfer to/);
    expect(validateRow({ ...row, toAccountId: 1 }, reference).second).toMatch(/differ/);
    expect(validateRow({ ...row, toAccountId: 2 }, reference)).toEqual({});
  });

  it("checks the date range and amount", () => {
    const row = filled(createFirstRow(reference), { when: "2026-03-07T10:00", amountText: "0" });
    const errors = validateRow(row, reference);
    expect(errors.when).toMatch(/between/);
    expect(errors.amount).toBeDefined();
  });

  it("rejects a budget that is not in the list", () => {
    expect(validateRow(filled(createFirstRow(reference), { budgetId: 99 }), reference).budget).toBeDefined();
  });
});

describe("isFieldApplicable", () => {
  it("limits the fields per type", () => {
    expect(isFieldApplicable("income", "second")).toBe(false);
    expect(isFieldApplicable("income", "target")).toBe(false);
    expect(isFieldApplicable("transfer", "second")).toBe(true);
    expect(isFieldApplicable("transfer", "budget")).toBe(false);
    expect(isFieldApplicable("expense", "budget")).toBe(true);
  });
});

describe("toSubmitRow", () => {
  it("builds the expense payload", () => {
    expect(toSubmitRow(filled(createFirstRow(reference), { budgetId: 13 }))).toEqual({
      type: "expense",
      dateTime: "2026-03-05T14:30",
      description: "Bakery",
      amount: 12.5,
      accountId: 1,
      fromAccountId: null,
      toAccountId: null,
      category: "Leven",
      tag: "David",
      budgetId: 13,
    });
  });

  it("uses from and to accounts for a transfer and drops the expense-only fields", () => {
    const row = filled(createFirstRow(reference), { type: "transfer", toAccountId: 2, budgetId: 13 });
    expect(toSubmitRow(row)).toMatchObject({
      type: "transfer",
      accountId: null,
      fromAccountId: 1,
      toAccountId: 2,
      category: null,
      tag: null,
      budgetId: null,
    });
  });
});

describe("gridReducer", () => {
  it("appends a row that carries over the sticky values and continues one minute later", () => {
    const { state } = setup();
    const first = filled(state.rows[0] as GridRow, { budgetId: 13 });
    const next = gridReducer({ ...state, rows: [first] }, { kind: "append" });
    const added = next.rows[1] as GridRow;
    expect(added).toMatchObject({
      type: "expense",
      when: "2026-03-05T14:31",
      accountId: 1,
      category: "Leven",
      tag: "David",
      budgetId: 13,
      description: "",
      amountText: "",
    });
    expect(added.id).not.toBe(first.id);
  });

  it("duplicates a row including its description and amount", () => {
    const { state } = setup();
    const first = filled(state.rows[0] as GridRow);
    const next = gridReducer({ ...state, rows: [first] }, { kind: "duplicate", id: first.id });
    expect(next.rows[1]).toMatchObject({ description: "Bakery", amountText: "12,50", when: "2026-03-05T14:31" });
  });

  it("moves rows and ignores moves past the ends", () => {
    let { state } = setup();
    state = gridReducer(state, { kind: "append" });
    const [a, b] = state.rows as [GridRow, GridRow];
    expect(gridReducer(state, { kind: "move", id: a.id, direction: -1 })).toBe(state);
    expect(gridReducer(state, { kind: "move", id: a.id, direction: 1 }).rows.map((r) => r.id)).toEqual([b.id, a.id]);
  });

  it("keeps one row when the last row is removed", () => {
    const { state } = setup();
    const first = filled(state.rows[0] as GridRow);
    const next = gridReducer({ ...state, rows: [first] }, { kind: "remove", id: first.id });
    expect(next.rows).toHaveLength(1);
    expect(next.rows[0]).toMatchObject({ description: "", accountId: 1, when: first.when });
  });

  it("clears a server error when that field is edited", () => {
    const { state } = setup();
    const first = { ...filled(state.rows[0] as GridRow), serverErrors: { amount: "Too much", account: "Bad" } };
    const next = gridReducer(
      { ...state, rows: [first] },
      { kind: "update", id: first.id, patch: { amountText: "1" }, field: "amount" },
    );
    expect((next.rows[0] as GridRow).serverErrors).toEqual({ account: "Bad" });
  });

  it("does not edit rows that were created", () => {
    const { state } = setup();
    const created: GridRow = { ...filled(state.rows[0] as GridRow), status: "created", createdId: 5 };
    const next = gridReducer(
      { ...state, rows: [created] },
      { kind: "update", id: created.id, patch: { description: "x" }, field: "description" },
    );
    expect((next.rows[0] as GridRow).description).toBe("Bakery");
  });

  it("applies results per status and moves blank rows after the last created row", () => {
    let { state } = setup();
    state = gridReducer(state, { kind: "append" });
    state = gridReducer(state, { kind: "append" });
    state = gridReducer(state, { kind: "append" });
    const [r1, r2, r3, blank] = state.rows.map((row, index) =>
      index < 3 ? filled(row, { description: `Row ${index}` }) : row,
    ) as [GridRow, GridRow, GridRow, GridRow];
    state = { ...state, rows: [r1, r2, r3, blank] };

    const next = gridReducer(state, {
      kind: "applyResults",
      sentIds: [r1.id, r2.id, r3.id],
      response: response([
        result(0, { status: "rejected", errors: { amount: ["Too big."], toAccountId: ["Pick one."] } }),
        result(1, { status: "uncertain", message: "Timed out." }),
        result(2, { dateTime: "2026-03-05T14:40", bumped: true }),
      ]),
    });

    const [a, b, c, d] = next.rows as [GridRow, GridRow, GridRow, GridRow];
    expect(a).toMatchObject({
      status: "draft",
      showErrors: true,
      serverErrors: { amount: "Too big.", second: "Pick one." },
    });
    expect(b.status).toBe("uncertain");
    expect(b.notice?.text).toMatch(/Check Firefly/);
    expect(c).toMatchObject({ status: "created", createdId: 102, when: "2026-03-05T14:40" });
    expect(c.notice?.text).toMatch(/14:40/);
    expect(d.when).toBe("2026-03-05T14:41");
  });

  it("shows validation messages only after an attempt, and hides them for created rows", () => {
    const { state } = setup();
    const first = state.rows[0] as GridRow;
    expect(visibleErrors(first, reference)).toEqual({});
    const shown = gridReducer({ ...state, rows: [{ ...first, description: "x" }] }, { kind: "showErrors" });
    expect(Object.keys(visibleErrors(shown.rows[0] as GridRow, reference))).toContain("amount");
    expect(visibleErrors({ ...(shown.rows[0] as GridRow), status: "created" }, reference)).toEqual({});
  });

  it("does not show errors on the blank row", () => {
    const { state } = setup();
    const shown = gridReducer(state, { kind: "showErrors" });
    expect((shown.rows[0] as GridRow).showErrors).toBe(false);
  });

  it("clears created rows, keeping at least one row", () => {
    const { state } = setup();
    const created: GridRow = { ...filled(state.rows[0] as GridRow), status: "created", createdId: 5 };
    const next = gridReducer({ ...state, rows: [created] }, { kind: "clearCreated" });
    expect(next.rows).toHaveLength(1);
    expect((next.rows[0] as GridRow).status).toBe("draft");
    expect((next.rows[0] as GridRow).when).toBe("2026-03-05T14:31");
  });

  it("resets uncertain rows to drafts", () => {
    const { state } = setup();
    const uncertain: GridRow = { ...filled(state.rows[0] as GridRow), status: "uncertain" };
    const next = gridReducer({ ...state, rows: [uncertain] }, { kind: "resetUncertain" });
    expect((next.rows[0] as GridRow).status).toBe("draft");
  });
});
