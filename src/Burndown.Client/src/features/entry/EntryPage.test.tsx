import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ReferenceData, SubmitResponse } from "./api";
import { EntryPage } from "./EntryPage";

const reference: ReferenceData = {
  now: "2026-03-05T14:40:00+01:00",
  today: "2026-03-05",
  timeZone: "Europe/Brussels",
  minDate: "2023-12-01",
  maxDate: "2026-04-30",
  maxBatchSize: 100,
  accounts: [
    { id: 1, name: "Checking" },
    { id: 2, name: "Savings" },
  ],
  categories: ["Leven", "Nodig"],
  targetTags: ["SharedDavid", "Tina"],
  budgets: [{ id: 13, name: "David" }],
};

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

function createdResponse(count: number): SubmitResponse {
  return {
    results: Array.from({ length: count }, (_, index) => ({
      index,
      status: "created",
      id: 100 + index,
      dateTime: null,
      bumped: false,
      code: null,
      message: null,
      errors: null,
    })),
    summary: { created: count, invalid: 0, rejected: 0, failed: 0, uncertain: 0, notAttempted: 0 },
    stopped: null,
  };
}

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <EntryPage />
    </QueryClientProvider>,
  );
}

describe("EntryPage", () => {
  let submitted: unknown[];
  let submitResponse: (rows: unknown[]) => Response;

  beforeEach(() => {
    submitted = [];
    submitResponse = (rows) => json(createdResponse(rows.length));
    vi.stubGlobal(
      "fetch",
      vi.fn(async (url: string, init?: RequestInit) => {
        if (url.startsWith("/api/entry/reference")) {
          return json(reference);
        }
        if (url.startsWith("/api/entry/suggest/descriptions")) {
          return json({ descriptions: [] });
        }
        if (url === "/api/entry/submit") {
          const body = JSON.parse(String(init?.body)) as { rows: unknown[] };
          submitted.push(...body.rows);
          return submitResponse(body.rows);
        }
        throw new Error(`Unexpected request ${url}`);
      }),
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("shows a first row dated by the server clock", async () => {
    renderPage();
    const when = await screen.findByRole("textbox", { name: "When" });
    expect(when).toHaveValue("05/03/2026 14:40");
    expect(screen.getByRole("grid", { name: "Entries to submit" })).toBeInTheDocument();
  });

  it("enters rows with the keyboard only and submits them in order with Ctrl+Enter", async () => {
    const user = userEvent.setup();
    renderPage();
    const first = await screen.findByRole("combobox", { name: "Description" });
    await user.click(first);

    await user.type(first, "Bakery");
    await user.keyboard("{Enter}");
    await user.keyboard("12,50{Enter}");
    await user.keyboard("che{Tab}"); // account
    await user.keyboard("leven{Tab}"); // category
    await user.keyboard("shared{Enter}"); // target
    await user.keyboard("{Enter}"); // budget (optional) → next row

    const descriptions = screen.getAllByRole("combobox", { name: "Description" });
    expect(descriptions).toHaveLength(2);
    expect(descriptions[1]).toHaveFocus();

    await user.keyboard("Butcher{Enter}7.5{Enter}");
    await user.keyboard("{Control>}{Enter}{/Control}");

    await waitFor(() => expect(submitted).toHaveLength(2));
    expect(submitted[0]).toMatchObject({
      type: "expense",
      dateTime: "2026-03-05T14:40",
      description: "Bakery",
      amount: 12.5,
      accountId: 1,
      category: "Leven",
      tag: "SharedDavid",
    });
    expect(submitted[1]).toMatchObject({
      description: "Butcher",
      amount: 7.5,
      dateTime: "2026-03-05T14:41",
      accountId: 1,
    });
    expect(await screen.findByText(/2 created in Firefly III/)).toBeInTheDocument();
    const rows = screen.getAllByRole("rowgroup").filter((group) => group.hasAttribute("data-status"));
    expect(rows.filter((group) => group.getAttribute("data-status") === "created")).toHaveLength(2);
    const blank = screen.getAllByRole("combobox", { name: "Description" }).at(-1);
    expect(blank).toHaveFocus();
    expect(blank).toHaveValue("");
  });

  it("adds a row when Tab leaves the last field of the last row", async () => {
    const user = userEvent.setup();
    renderPage();
    const first = await screen.findByRole("combobox", { name: "Description" });
    await user.type(first, "Bakery");
    await user.keyboard("{Enter}12,50{Enter}che{Tab}leven{Tab}shared{Enter}");
    expect(screen.getAllByRole("combobox", { name: "Description" })).toHaveLength(1);

    await user.keyboard("{Tab}"); // budget, the last field

    const descriptions = screen.getAllByRole("combobox", { name: "Description" });
    expect(descriptions).toHaveLength(2);
    expect(descriptions[1]).toHaveFocus();
  });

  it("keeps the focus in an empty last row when Tab leaves its last field", async () => {
    const user = userEvent.setup();
    renderPage();
    const first = await screen.findByRole("combobox", { name: "Description" });
    await user.click(first);
    for (let i = 0; i < 6; i++) {
      await user.keyboard("{Tab}");
    }

    const descriptions = screen.getAllByRole("combobox", { name: "Description" });
    expect(descriptions).toHaveLength(1);
    expect(descriptions[0]).toHaveFocus();
  });

  it("duplicates the row with Ctrl+D and keeps the focus in the copy", async () => {
    const user = userEvent.setup();
    renderPage();
    const first = await screen.findByRole("combobox", { name: "Description" });
    await user.type(first, "Bakery");
    await user.keyboard("{Enter}12,50");
    await user.keyboard("{Control>}d{/Control}");

    const descriptions = screen.getAllByRole("combobox", { name: "Description" });
    expect(descriptions).toHaveLength(2);
    expect(descriptions[0]).toHaveValue("Bakery");
    expect(descriptions[1]).toHaveValue("Bakery");
    expect(descriptions[1]).toHaveFocus();
  });

  it("treats an empty row before the last one as invalid", async () => {
    const user = userEvent.setup();
    renderPage();
    const first = await screen.findByRole("combobox", { name: "Description" });
    await user.click(first);
    await user.keyboard("{Control>}d{/Control}"); // a copy of the empty row, so the first row is no longer last
    await user.keyboard("{Control>}{Enter}{/Control}");

    expect(await screen.findByText(/1 row needs attention/)).toBeInTheDocument();
    expect(submitted).toHaveLength(0);
  });

  it("sends nothing and focuses the first problem when a row is incomplete", async () => {
    const user = userEvent.setup();
    renderPage();
    const first = await screen.findByRole("combobox", { name: "Description" });
    await user.type(first, "Bakery");
    await user.keyboard("{Control>}{Enter}{/Control}");

    expect(await screen.findByText(/1 row needs attention/)).toBeInTheDocument();
    expect(submitted).toHaveLength(0);
    expect(screen.getByRole("textbox", { name: "Amount" })).toHaveFocus();
    expect(screen.getByRole("textbox", { name: "Amount" })).toHaveAttribute("aria-invalid", "true");
  });

  it("keeps rows that the server rejects and shows the message at the field", async () => {
    submitResponse = () =>
      json({
        results: [
          {
            index: 0,
            status: "rejected",
            id: null,
            dateTime: null,
            bumped: false,
            code: "firefly.validation",
            message: "Rejected",
            errors: { accountId: ["The source account is not valid."] },
          },
        ],
        summary: { created: 0, invalid: 0, rejected: 1, failed: 0, uncertain: 0, notAttempted: 0 },
        stopped: null,
      });
    const user = userEvent.setup();
    renderPage();
    const first = await screen.findByRole("combobox", { name: "Description" });
    await user.type(first, "Bakery");
    await user.keyboard("{Enter}5{Enter}che{Tab}leven{Tab}shared{Tab}");
    await user.keyboard("{Control>}{Enter}{/Control}");

    expect(await screen.findByText(/The source account is not valid\./)).toBeInTheDocument();
    expect(screen.getAllByRole("combobox", { name: "Account" })[0]).toHaveFocus();
  });

  it("marks unconfirmed rows and never resends them", async () => {
    submitResponse = () =>
      json({
        results: [
          {
            index: 0,
            status: "uncertain",
            id: null,
            dateTime: null,
            bumped: false,
            code: "firefly.timeout",
            message: "Firefly III did not answer in time.",
            errors: null,
          },
        ],
        summary: { created: 0, invalid: 0, rejected: 0, failed: 0, uncertain: 1, notAttempted: 0 },
        stopped: { atRow: 0, code: "firefly.timeout", message: "Firefly III did not answer in time." },
      });
    const user = userEvent.setup();
    renderPage();
    const first = await screen.findByRole("combobox", { name: "Description" });
    await user.type(first, "Bakery");
    await user.keyboard("{Enter}5{Enter}che{Tab}leven{Tab}shared{Tab}");
    await user.keyboard("{Control>}{Enter}{/Control}");

    expect(await screen.findByText(/Check Firefly III before sending it again/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Treat 1 unconfirmed as not sent/ })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Submit 0 rows/ })).toBeDisabled();
  });

  it("reports an unreachable server without losing the rows", async () => {
    submitResponse = () => {
      throw new TypeError("Failed to fetch");
    };
    const user = userEvent.setup();
    renderPage();
    const first = await screen.findByRole("combobox", { name: "Description" });
    await user.type(first, "Bakery");
    await user.keyboard("{Enter}5{Enter}che{Tab}leven{Tab}shared{Tab}");
    await user.keyboard("{Control>}{Enter}{/Control}");

    expect(await screen.findByText(/some rows may already have been created/)).toBeInTheDocument();
    expect(screen.getByRole("combobox", { name: "Description" })).toHaveValue("Bakery");
  });
});
