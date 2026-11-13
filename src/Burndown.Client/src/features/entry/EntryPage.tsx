import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useLayoutEffect, useMemo, useReducer, useRef, useState } from "react";
import { ApiError } from "../overview/api";
import { describeError, ErrorPanel } from "../overview/ErrorPanel";
import { fetchReference, type ReferenceData, refreshReference, submitRows } from "./api";
import { buildLists, GRID_COLUMNS, GridRow } from "./GridRow";
import { gridReducer, initialGrid } from "./gridReducer";
import {
  createFirstRow,
  FIELD_ORDER,
  type GridRow as GridRowModel,
  isBlank,
  isFieldApplicable,
  type Notice,
  pendingRows,
  toSubmitRow,
  type UiField,
  validateRow,
} from "./model";
import { ShortcutLegend } from "./ShortcutLegend";

type FocusRequest = { rowId: number; field: UiField } | { rowId: number; after: UiField };

function isUiField(value: string | undefined): value is UiField {
  return FIELD_ORDER.some((field) => field === value);
}

function findInput(rowId: number, field: UiField): HTMLInputElement | null {
  return document.querySelector<HTMLInputElement>(`[data-entry-row="${rowId}"][data-entry-field="${field}"]`);
}

function applicableFields(row: GridRowModel): UiField[] {
  return FIELD_ORDER.filter((field) => isFieldApplicable(row.type, field));
}

function EntryGrid({ reference, onReload }: { reference: ReferenceData; onReload: () => Promise<void> }) {
  const [state, dispatch] = useReducer(gridReducer, reference, (ref) => initialGrid(createFirstRow(ref)));
  const [banner, setBanner] = useState<Notice | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [reloading, setReloading] = useState(false);
  const [submitTick, setSubmitTick] = useState(0);
  const [, setFocusTick] = useState(0);
  const lists = useMemo(() => buildLists(reference), [reference]);

  const stateRef = useRef(state);
  stateRef.current = state;
  const referenceRef = useRef(reference);
  referenceRef.current = reference;
  const submittingRef = useRef(false);
  const focusRequest = useRef<FocusRequest | null>(null);

  useLayoutEffect(() => {
    const request = focusRequest.current;
    if (!request) {
      return;
    }
    let field: UiField | null;
    if ("field" in request) {
      field = request.field;
    } else {
      const row = state.rows.find((candidate) => candidate.id === request.rowId);
      const fields = row ? applicableFields(row) : [];
      field = fields[fields.indexOf(request.after) + 1] ?? null;
    }
    if (field === null) {
      focusRequest.current = null;
      return;
    }
    const input = findInput(request.rowId, field);
    if (input && !input.disabled) {
      focusRequest.current = null;
      input.focus();
    }
  });

  const requestFocus = (request: FocusRequest) => {
    focusRequest.current = request;
    setFocusTick((tick) => tick + 1);
  };

  const advance = (rowId: number, field: UiField) => {
    const { rows, nextId } = stateRef.current;
    const index = rows.findIndex((row) => row.id === rowId);
    const row = rows[index];
    if (!row) {
      return;
    }
    const fields = applicableFields(row);
    if (field !== fields[fields.length - 1]) {
      requestFocus({ rowId, after: field });
      return;
    }
    const nextRow = rows.slice(index + 1).find((candidate) => candidate.status === "draft");
    if (nextRow) {
      requestFocus({ rowId: nextRow.id, field: "description" });
    } else if (!isBlank(row)) {
      requestFocus({ rowId: nextId, field: "description" });
      dispatch({ kind: "append" });
    }
  };

  const rowAction = (id: number, action: "up" | "down" | "duplicate" | "remove", field: UiField = "description") => {
    const { rows, nextId } = stateRef.current;
    const index = rows.findIndex((row) => row.id === id);
    if (index < 0) {
      return;
    }
    switch (action) {
      case "up":
      case "down":
        requestFocus({ rowId: id, field });
        dispatch({ kind: "move", id, direction: action === "up" ? -1 : 1 });
        return;
      case "duplicate":
        requestFocus({ rowId: nextId, field: "description" });
        dispatch({ kind: "duplicate", id });
        return;
      case "remove": {
        const neighbour = rows[index + 1] ?? rows[index - 1];
        requestFocus({ rowId: neighbour ? neighbour.id : nextId, field: "description" });
        dispatch({ kind: "remove", id });
        return;
      }
    }
  };

  const focusAdjacent = (rowId: number, field: UiField, direction: -1 | 1) => {
    const { rows } = stateRef.current;
    const target = rows[rows.findIndex((row) => row.id === rowId) + direction];
    if (!target) {
      return;
    }
    const usable = isFieldApplicable(target.type, field) && target.status === "draft";
    findInput(target.id, usable ? field : "description")?.focus();
  };

  const runSubmit = async () => {
    if (submittingRef.current) {
      return;
    }
    const current = stateRef.current;
    const ref = referenceRef.current;
    const pending = pendingRows(current.rows);
    if (pending.length === 0) {
      setBanner({ tone: "muted", text: "Nothing to submit: fill in a description and an amount first." });
      return;
    }
    const invalid = pending.filter((row) => Object.keys(validateRow(row, ref)).length > 0);
    if (invalid.length > 0) {
      dispatch({ kind: "showErrors" });
      setBanner({
        tone: "bad",
        text: `${invalid.length} ${invalid.length === 1 ? "row needs" : "rows need"} attention. Nothing was sent.`,
      });
      const first = invalid[0];
      if (first) {
        const errors = validateRow(first, ref);
        const field = FIELD_ORDER.find((candidate) => errors[candidate] !== undefined);
        if (field) {
          findInput(first.id, field)?.focus();
        }
      }
      return;
    }

    const batch = pending.slice(0, ref.maxBatchSize);
    submittingRef.current = true;
    setSubmitting(true);
    setBanner(null);
    try {
      const response = await submitRows(batch.map(toSubmitRow));
      const action = { kind: "applyResults", sentIds: batch.map((row) => row.id), response } as const;
      dispatch(action);

      const next = gridReducer(stateRef.current, action);
      const last = next.rows[next.rows.length - 1];
      const failed = next.rows.find((row) => row.status === "draft" && Object.keys(row.serverErrors).length > 0);
      if (last && !isBlank(last)) {
        dispatch({ kind: "append" });
        requestFocus({ rowId: next.nextId, field: "description" });
      } else if (last) {
        requestFocus({ rowId: last.id, field: "description" });
      }
      if (failed) {
        const field = FIELD_ORDER.find((candidate) => failed.serverErrors[candidate] !== undefined);
        if (field) {
          requestFocus({ rowId: failed.id, field });
        }
      }

      const { summary, stopped } = response;
      const problems = summary.invalid + summary.rejected + summary.failed;
      const parts = [`${summary.created} created in Firefly III`];
      if (problems > 0) {
        parts.push(`${problems} need attention`);
      }
      if (summary.uncertain > 0) {
        parts.push(`${summary.uncertain} unconfirmed`);
      }
      if (summary.notAttempted > 0) {
        parts.push(`${summary.notAttempted} not sent`);
      }
      let text = `${parts.join(", ")}.`;
      if (stopped) {
        text = `${text} ${describeError(new ApiError(0, stopped.code, stopped.message)).title}: ${stopped.message}`;
      }
      if (pending.length > batch.length) {
        text = `${text} ${pending.length - batch.length} more rows are waiting: submit again.`;
      }
      setBanner({
        tone: stopped || summary.uncertain > 0 ? "warn" : problems > 0 ? "bad" : "muted",
        text,
      });
    } catch (error) {
      const { title } = describeError(error, "The rows could not be submitted");
      const detail = error instanceof Error && error.message ? ` ${error.message}` : "";
      const unsure =
        error instanceof ApiError && error.code === "network"
          ? " Check Firefly III before submitting again: some rows may already have been created."
          : "";
      setBanner({ tone: "bad", text: `${title}.${detail}${unsure}` });
    } finally {
      submittingRef.current = false;
      setSubmitting(false);
    }
  };

  // Submit runs from an effect so that blur handlers (When, Amount) have committed their values first.
  const lastTick = useRef(0);
  // biome-ignore lint/correctness/useExhaustiveDependencies: runs only when a new submit is requested
  useEffect(() => {
    if (submitTick !== lastTick.current) {
      lastTick.current = submitTick;
      void runSubmit();
    }
  }, [submitTick]);

  const requestSubmit = () => {
    if (document.activeElement instanceof HTMLElement) {
      document.activeElement.blur();
    }
    setSubmitTick((tick) => tick + 1);
  };

  const onKeyDown = (event: React.KeyboardEvent<HTMLDivElement>) => {
    const target = event.target;
    if (!(target instanceof HTMLElement)) {
      return;
    }
    const rowId = Number(target.dataset.entryRow);
    const field = target.dataset.entryField;
    if (Number.isNaN(rowId) || !isUiField(field)) {
      return;
    }

    if (event.key === "Enter" && event.ctrlKey && !event.altKey) {
      event.preventDefault();
      requestSubmit();
      return;
    }
    if (event.key === "Enter" && !event.ctrlKey && !event.altKey && !event.shiftKey) {
      if (!event.defaultPrevented) {
        event.preventDefault();
        advance(rowId, field);
      }
      return;
    }
    if (event.key === "Tab" && !event.ctrlKey && !event.altKey && !event.shiftKey && !event.defaultPrevented) {
      // Tabbing out of the last field of the last row starts a new row instead of leaving the grid.
      const { rows } = stateRef.current;
      const row = rows[rows.length - 1];
      if (row && row.id === rowId && row.status === "draft") {
        const fields = applicableFields(row);
        if (field === fields[fields.length - 1]) {
          event.preventDefault();
          if (isBlank(row)) {
            // An empty row is not worth a copy: it would become an invalid empty row. Stay in the grid instead.
            findInput(rowId, "description")?.focus();
          } else {
            advance(rowId, field);
          }
        }
      }
      return;
    }
    if (event.ctrlKey && !event.altKey && !event.shiftKey && event.key.toLowerCase() === "d") {
      event.preventDefault();
      rowAction(rowId, "duplicate");
      return;
    }
    if (event.altKey && !event.ctrlKey) {
      if (event.key === "ArrowUp" || event.key === "ArrowDown") {
        event.preventDefault();
        rowAction(rowId, event.key === "ArrowUp" ? "up" : "down", field);
      } else if (event.key === "Delete" || event.key === "Backspace") {
        event.preventDefault();
        rowAction(rowId, "remove");
      }
      return;
    }
    if (event.ctrlKey && !event.altKey && (event.key === "ArrowUp" || event.key === "ArrowDown")) {
      event.preventDefault();
      focusAdjacent(rowId, field, event.key === "ArrowUp" ? -1 : 1);
    }
  };

  const ready = pendingRows(state.rows).length;
  const created = state.rows.filter((row) => row.status === "created").length;
  const uncertain = state.rows.filter((row) => row.status === "uncertain").length;

  const bannerTone = banner?.tone === "bad" ? "text-bad" : banner?.tone === "warn" ? "text-amber-300" : "text-muted";

  return (
    <>
      <div className="overflow-x-auto rounded-lg border border-border">
        <div role="grid" aria-label="Entries to submit" onKeyDown={onKeyDown} className="min-w-[960px] px-2 pb-1">
          <div role="rowgroup">
            <div
              role="row"
              className="grid gap-1.5 border-b border-border py-2 text-xs font-medium uppercase tracking-wide text-muted"
              style={{ gridTemplateColumns: GRID_COLUMNS }}
            >
              <div role="columnheader" aria-label="Row number" className="text-center">
                #
              </div>
              <div role="columnheader">Type</div>
              <div role="columnheader">When</div>
              <div role="columnheader">Description</div>
              <div role="columnheader" className="text-right">
                Amount
              </div>
              <div role="columnheader">Account</div>
              <div role="columnheader">Category / to</div>
              <div role="columnheader">Target</div>
              <div role="columnheader">Budget</div>
            </div>
          </div>
          {state.rows.map((row, index) => (
            <GridRow
              key={row.id}
              row={row}
              index={index}
              count={state.rows.length}
              reference={reference}
              lists={lists}
              onChange={(id, patch, field) => dispatch({ kind: "update", id, patch, field })}
              onAction={(id, action) => rowAction(id, action)}
            />
          ))}
        </div>
      </div>

      <div className="mt-4 flex flex-wrap items-center gap-3">
        <button
          type="button"
          disabled={submitting || ready === 0}
          onClick={requestSubmit}
          className="rounded-md bg-accent px-4 py-2 text-sm font-semibold text-surface hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-40"
        >
          {submitting ? "Submitting…" : `Submit ${ready} ${ready === 1 ? "row" : "rows"}`}
          <span className="ml-2 font-normal opacity-70">Ctrl+Enter</span>
        </button>
        <button
          type="button"
          disabled={created === 0}
          onClick={() => dispatch({ kind: "clearCreated" })}
          className="rounded-md border border-border bg-surface-raised px-3 py-2 text-sm hover:border-accent disabled:opacity-40"
        >
          Clear submitted ({created})
        </button>
        {uncertain > 0 ? (
          <button
            type="button"
            onClick={() => dispatch({ kind: "resetUncertain" })}
            className="rounded-md border border-amber-300/60 bg-surface-raised px-3 py-2 text-sm text-amber-300 hover:border-amber-300"
          >
            Treat {uncertain} unconfirmed as not sent
          </button>
        ) : null}
        <button
          type="button"
          disabled={reloading}
          onClick={async () => {
            setReloading(true);
            try {
              await onReload();
              setBanner({ tone: "muted", text: "Accounts, categories, tags and budgets were reloaded." });
            } catch (error) {
              setBanner({ tone: "bad", text: `${describeError(error, "Reloading failed").title}.` });
            } finally {
              setReloading(false);
            }
          }}
          className="ml-auto rounded-md border border-border bg-surface-raised px-3 py-2 text-sm hover:border-accent disabled:opacity-40"
        >
          Reload accounts
        </button>
      </div>
      <p role="status" aria-live="polite" className={`mt-2 min-h-5 text-sm ${bannerTone}`}>
        {banner?.text ?? ""}
      </p>

      <ShortcutLegend />
    </>
  );
}

export function EntryPage() {
  const queryClient = useQueryClient();
  const query = useQuery({
    queryKey: ["entry", "reference"],
    queryFn: ({ signal }) => fetchReference(signal),
    staleTime: 5 * 60_000,
  });

  const reload = async () => {
    const fresh = await refreshReference();
    queryClient.setQueryData(["entry", "reference"], fresh);
  };

  return (
    <div className="mx-auto max-w-[1600px] px-6 py-6">
      <h1 className="mb-6 text-2xl font-semibold">Entry</h1>
      {query.isPending ? (
        <div
          role="status"
          aria-busy="true"
          aria-label="Loading entry"
          className="h-64 animate-pulse rounded-lg bg-surface-raised"
        />
      ) : query.isError ? (
        <ErrorPanel
          error={query.error}
          onRetry={() => void query.refetch()}
          fallbackTitle="The entry form could not be loaded"
        />
      ) : (
        <EntryGrid reference={query.data} onReload={reload} />
      )}
    </div>
  );
}
