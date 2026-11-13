export type PerDayTone = "none" | "green" | "red";

export interface BudgetSummary {
  income: number;
  installments: number;
  computed: number;
  effective: number;
  overridden: boolean;
}

export interface DayRow {
  day: number;
  date: string;
  isToday: boolean;
  isWeekend: boolean;
  isFuture: boolean;
  amount: number;
  description: string;
  cumulativeBudget: number;
  runningTotal: number;
  perDay: number;
  perDayTone: PerDayTone;
  perDayAlpha: number;
  amountAlpha: number;
}

export interface MonthTotals {
  spent: number;
  remaining: number;
  dailyBudget: number;
}

export interface ChartPoint {
  day: number;
  breakEven: number;
  actual: number | null;
}

export interface BurndownChartData {
  points: ChartPoint[];
  todayDay: number | null;
}

export interface OverviewResponse {
  month: string;
  today: string;
  minimumMonth: string;
  maximumMonth: string;
  currentMonth: string;
  budget: BudgetSummary;
  days: DayRow[];
  totals: MonthTotals;
  chart: BurndownChartData;
}

export interface MonthResult {
  month: string;
  result: number;
}

export interface YearOverviewResponse {
  month: string;
  year: number;
  total: number;
  months: MonthResult[];
}

/** A failed call to the BFF. `code` is the application error code, or `network` / `unknown`. */
export class ApiError extends Error {
  readonly status: number;
  readonly code: string;
  readonly fieldErrors: Record<string, string[]>;

  constructor(status: number, code: string, message: string, fieldErrors: Record<string, string[]> = {}) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.code = code;
    this.fieldErrors = fieldErrors;
  }
}

interface ProblemBody {
  code?: unknown;
  detail?: unknown;
  errors?: unknown;
}

export async function toApiError(response: Response): Promise<ApiError> {
  let body: ProblemBody = {};
  try {
    body = (await response.json()) as ProblemBody;
  } catch {
    // Not JSON (for example an HTML error page): fall through to the generic error.
  }
  const code = typeof body.code === "string" ? body.code : "unknown";
  const detail = typeof body.detail === "string" ? body.detail : `The server answered ${response.status}.`;
  const errors = body.errors && typeof body.errors === "object" ? (body.errors as Record<string, string[]>) : undefined;
  return new ApiError(response.status, code, detail, errors);
}

async function getJson<T>(url: string, signal: AbortSignal): Promise<T> {
  let response: Response;
  try {
    response = await fetch(url, { signal, headers: { Accept: "application/json" } });
  } catch (error) {
    if (signal.aborted) {
      throw error;
    }
    throw new ApiError(0, "network", "The Burndown server could not be reached.");
  }

  if (!response.ok) {
    throw await toApiError(response);
  }
  return (await response.json()) as T;
}

/** Fetches the overview. `month` is `yyyy-MM` (null = the server's current month); `budgetOverride` is invariant text. */
export function fetchOverview(
  month: string | null,
  budgetOverride: string | null,
  signal: AbortSignal,
): Promise<OverviewResponse> {
  const params = new URLSearchParams();
  if (month !== null) {
    params.set("month", month);
  }
  if (budgetOverride !== null) {
    params.set("budgetOverride", budgetOverride);
  }
  const query = params.size > 0 ? `?${params.toString()}` : "";
  return getJson<OverviewResponse>(`/api/overview${query}`, signal);
}

/** Fetches the results of the months before `month` (`yyyy-MM`; null = the server's current month). */
export function fetchYearOverview(month: string | null, signal: AbortSignal): Promise<YearOverviewResponse> {
  const query = month === null ? "" : `?${new URLSearchParams({ month }).toString()}`;
  return getJson<YearOverviewResponse>(`/api/overview/year${query}`, signal);
}
