import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { App } from "../../App";
import type { OverviewResponse, YearOverviewResponse } from "./api";

// Testing Library normalizes the non-breaking space in the rendered text to a regular space.
function overview(month: string, overrides: Partial<OverviewResponse> = {}): OverviewResponse {
  return {
    month,
    today: "2024-03-02",
    minimumMonth: "2023-12",
    maximumMonth: "2024-04",
    currentMonth: "2024-03",
    budget: { income: 3200, installments: 100, computed: 3100, effective: 3100, overridden: false },
    days: [
      {
        day: 1,
        date: `${month}-01`,
        isToday: false,
        isWeekend: false,
        isFuture: false,
        amount: 25.5,
        description: "Bakery, Groceries",
        cumulativeBudget: 100,
        runningTotal: 25.5,
        perDay: 74.5,
        perDayTone: "green",
        perDayAlpha: 0.4,
        amountAlpha: 0.2,
      },
      {
        day: 2,
        date: `${month}-02`,
        isToday: true,
        isWeekend: true,
        isFuture: false,
        amount: 0,
        description: "",
        cumulativeBudget: 200,
        runningTotal: 25.5,
        perDay: 174.5,
        perDayTone: "green",
        perDayAlpha: 0.8,
        amountAlpha: 0,
      },
    ],
    totals: { spent: 25.5, remaining: 3074.5, dailyBudget: 100 },
    chart: {
      points: [
        { day: 0, breakEven: 3100, actual: 3100 },
        { day: 1, breakEven: 3000, actual: 3074.5 },
        { day: 2, breakEven: 2900, actual: null },
      ],
      todayDay: 2,
    },
    ...overrides,
  };
}

function yearOverview(month: string): YearOverviewResponse {
  if (month === "2023-12") {
    return { month, year: 2023, total: 0, months: [] };
  }
  const january = month.endsWith("-01");
  return {
    month,
    year: january ? Number(month.slice(0, 4)) - 1 : Number(month.slice(0, 4)),
    total: 2300,
    months: [
      { month: "2024-01", result: 2500 },
      { month: "2024-02", result: -200 },
    ],
  };
}

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

function requestedUrls(fetchMock: ReturnType<typeof vi.fn>): string[] {
  return fetchMock.mock.calls.map((call) => String(call[0]));
}

function renderApp() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } } });
  return render(
    <QueryClientProvider client={client}>
      <App />
    </QueryClientProvider>,
  );
}

describe("Overview page", () => {
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    fetchMock = vi.fn(async (url: string) => {
      const parsed = new URL(url, "http://localhost");
      const month = parsed.searchParams.get("month") ?? "2024-03";
      return json(parsed.pathname === "/api/overview/year" ? yearOverview(month) : overview(month));
    });
    vi.stubGlobal("fetch", fetchMock);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("shows a loading state and then the month, summary and daily table", async () => {
    renderApp();

    expect(screen.getByRole("status", { name: "Loading overview" })).toBeInTheDocument();
    expect(await screen.findByRole("heading", { name: "March 2024" })).toBeInTheDocument();

    const summary = screen.getByLabelText("Month summary");
    expect(within(summary).getByText(`€ 3.100,00`)).toBeInTheDocument();

    const table = screen.getByRole("table", { name: "Daily expenses and remaining budget" });
    expect(within(table).getByText("Bakery, Groceries")).toBeInTheDocument();
    expect(within(table).getByText(`€ 25,50`, { selector: "td:nth-child(3)" })).toBeInTheDocument();
    expect(within(table).getByText("01/03/2024")).toBeInTheDocument();
  });

  it("marks today's row", async () => {
    renderApp();
    await screen.findByRole("heading", { name: "March 2024" });

    const todayRow = screen.getByRole("row", { current: "date" });
    expect(within(todayRow).getByText("02/03/2024")).toBeInTheDocument();
    expect(within(todayRow).getByText("Today")).toBeInTheDocument();
  });

  it("offers the chart data as an accessible table", async () => {
    renderApp();
    await screen.findByRole("heading", { name: "March 2024" });

    const chartTable = screen.getByRole("table", { name: "Burndown chart data", hidden: true });
    expect(within(chartTable).getAllByRole("row", { hidden: true })).toHaveLength(4);
  });

  it("navigates months with the buttons and requests the chosen month", async () => {
    const user = userEvent.setup();
    renderApp();
    await screen.findByRole("heading", { name: "March 2024" });

    await user.click(screen.getByRole("button", { name: "Previous month" }));

    expect(await screen.findByRole("heading", { name: "February 2024" })).toBeInTheDocument();
    expect(requestedUrls(fetchMock).at(-1)).toContain("month=2024-02");
  });

  it("navigates months with the arrow keys and Home", async () => {
    const user = userEvent.setup();
    renderApp();
    await screen.findByRole("heading", { name: "March 2024" });

    await user.keyboard("{ArrowRight}");
    expect(await screen.findByRole("heading", { name: "April 2024" })).toBeInTheDocument();

    await user.keyboard("{Home}");
    expect(await screen.findByRole("heading", { name: "March 2024" })).toBeInTheDocument();
  });

  it("disables navigation at the first and last month", async () => {
    const user = userEvent.setup();
    renderApp();
    await screen.findByRole("heading", { name: "March 2024" });

    await user.keyboard("{ArrowRight}");
    await screen.findByRole("heading", { name: "April 2024" });
    expect(screen.getByRole("button", { name: "Next month" })).toBeDisabled();

    await user.keyboard("{ArrowRight}");
    expect(screen.getByRole("heading", { name: "April 2024" })).toBeInTheDocument();
  });

  it("sends a typed override with an invariant decimal point and clears it when the month changes", async () => {
    const user = userEvent.setup();
    renderApp();
    await screen.findByRole("heading", { name: "March 2024" });

    const field = screen.getByLabelText("Budget override");
    await user.type(field, "1500,5");
    await vi.waitFor(() => expect(requestedUrls(fetchMock).at(-1)).toContain("budgetOverride=1500.5"));

    await user.click(screen.getByRole("button", { name: "Previous month" }));
    await screen.findByRole("heading", { name: "February 2024" });
    expect(field).toHaveValue("");
    expect(requestedUrls(fetchMock).at(-1)).not.toContain("budgetOverride");
  });

  it("shows an inline message for a malformed override without calling the server", async () => {
    const user = userEvent.setup();
    renderApp();
    await screen.findByRole("heading", { name: "March 2024" });
    await screen.findByLabelText("Result per month");
    const calls = fetchMock.mock.calls.length;

    await user.type(screen.getByLabelText("Budget override"), "abc");

    expect(await screen.findByText(/Enter an amount/)).toBeInTheDocument();
    expect(screen.getByLabelText("Budget override")).toBeInvalid();
    expect(fetchMock.mock.calls.length).toBe(calls);
  });

  it("explains an invalid or expired token", async () => {
    fetchMock.mockResolvedValue(
      json({ code: "firefly.unauthorized", detail: "Firefly III rejected the token (401)." }, 502),
    );
    renderApp();

    const alert = await screen.findByRole("alert");
    expect(within(alert).getByText("Firefly III rejected the access token")).toBeInTheDocument();
    expect(within(alert).getByText(/Personal Access Tokens/)).toBeInTheDocument();
    expect(within(alert).getByText("Firefly III rejected the token (401).")).toBeInTheDocument();
  });

  it("explains an unreachable Firefly and lets the user retry", async () => {
    const user = userEvent.setup();
    fetchMock.mockResolvedValueOnce(json({ code: "firefly.unavailable", detail: "Firefly III is unreachable." }, 503));
    renderApp();

    expect(await screen.findByText("Firefly III is not reachable")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Try again" }));

    expect(await screen.findByRole("heading", { name: "March 2024" })).toBeInTheDocument();
  });

  it("explains an unreachable Burndown server", async () => {
    fetchMock.mockRejectedValue(new TypeError("Failed to fetch"));
    renderApp();

    expect(await screen.findByText("The Burndown server is not reachable")).toBeInTheDocument();
  });

  it("shows the year result with a total and one entry per earlier month", async () => {
    renderApp();

    const block = await screen.findByRole("region", { name: "Year overview" });
    expect(await within(block).findByText("Year 2024, result so far")).toBeInTheDocument();
    expect(within(block).getByText(`€ 2.300,00`)).toBeInTheDocument();
    const entries = within(within(block).getByLabelText("Result per month")).getAllByRole("listitem");
    expect(entries).toHaveLength(2);
    expect(within(entries[0] as HTMLElement).getByText("Jan")).toBeInTheDocument();
    expect(within(entries[1] as HTMLElement).getByText(`€ -200,00`)).toBeInTheDocument();
  });

  it("follows the viewed month and labels the previous year in January", async () => {
    const user = userEvent.setup();
    renderApp();
    await screen.findByRole("heading", { name: "March 2024" });

    for (let step = 0; step < 2; step++) {
      await user.click(screen.getByRole("button", { name: "Previous month" }));
    }
    await screen.findByRole("heading", { name: "January 2024" });

    expect(await screen.findByText("Previous year 2023, result")).toBeInTheDocument();
    expect(requestedUrls(fetchMock)).toContain("/api/overview/year?month=2024-01");
  });

  it("says there is no previous data in the first month", async () => {
    const user = userEvent.setup();
    renderApp();
    await screen.findByRole("heading", { name: "March 2024" });

    for (let step = 0; step < 3; step++) {
      await user.click(screen.getByRole("button", { name: "Previous month" }));
    }

    expect(await screen.findByText("No previous data")).toBeInTheDocument();
  });

  it("keeps the month overview when the year result fails", async () => {
    fetchMock.mockImplementation(async (url: string) => {
      const parsed = new URL(url, "http://localhost");
      return parsed.pathname === "/api/overview/year"
        ? json({ code: "firefly.timeout", detail: "Too slow." }, 504)
        : json(overview(parsed.searchParams.get("month") ?? "2024-03"));
    });
    renderApp();

    expect(await screen.findByRole("heading", { name: "March 2024" })).toBeInTheDocument();
    const block = await screen.findByRole("region", { name: "Year overview" });
    expect(await within(block).findByText(/took too long/)).toBeInTheDocument();
    expect(screen.getByLabelText("Month summary")).toBeInTheDocument();
  });

  it("names the dotted chart line Break-even pace", async () => {
    renderApp();
    await screen.findByRole("heading", { name: "March 2024" });

    expect(screen.getAllByText("Break-even pace", { exact: false }).length).toBeGreaterThan(0);
    expect(screen.queryByText("Ideal")).not.toBeInTheDocument();
  });

  it("says so when the month has no expenses", async () => {
    fetchMock.mockImplementation(async () => {
      const empty = overview("2024-03");
      return json({ ...empty, days: empty.days.map((day) => ({ ...day, amount: 0, description: "" })) });
    });
    renderApp();

    expect(await screen.findByText("No expenses in this month yet.")).toBeInTheDocument();
  });
});
