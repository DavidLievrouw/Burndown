import { ApiError, toApiError } from "../overview/api";

export type RowType = "expense" | "income" | "transfer";

export interface AccountItem {
  id: number;
  name: string;
}

export interface BudgetItem {
  id: number;
  name: string;
}

export interface ReferenceData {
  now: string;
  today: string;
  timeZone: string;
  minDate: string;
  maxDate: string;
  maxBatchSize: number;
  accounts: AccountItem[];
  categories: string[];
  targetTags: string[];
  budgets: BudgetItem[];
}

export interface SubmitRow {
  type: RowType;
  dateTime: string;
  description: string;
  amount: number;
  accountId: number | null;
  fromAccountId: number | null;
  toAccountId: number | null;
  category: string | null;
  tag: string | null;
  budgetId: number | null;
}

export type RowStatus = "created" | "invalid" | "rejected" | "failed" | "uncertain" | "notAttempted";

export interface RowResult {
  index: number;
  status: RowStatus;
  id: number | null;
  dateTime: string | null;
  bumped: boolean;
  code: string | null;
  message: string | null;
  errors: Record<string, string[]> | null;
}

export interface SubmitSummary {
  created: number;
  invalid: number;
  rejected: number;
  failed: number;
  uncertain: number;
  notAttempted: number;
}

export interface BatchStop {
  atRow: number;
  code: string;
  message: string;
}

export interface SubmitResponse {
  results: RowResult[];
  summary: SubmitSummary;
  stopped: BatchStop | null;
}

async function request<T>(url: string, init: RequestInit): Promise<T> {
  let response: Response;
  try {
    response = await fetch(url, { ...init, headers: { Accept: "application/json", ...init.headers } });
  } catch (error) {
    if (init.signal?.aborted) {
      throw error;
    }
    throw new ApiError(0, "network", "The Burndown server could not be reached.");
  }
  if (!response.ok) {
    throw await toApiError(response);
  }
  return (await response.json()) as T;
}

export function fetchReference(signal: AbortSignal): Promise<ReferenceData> {
  return request<ReferenceData>("/api/entry/reference", { signal });
}

/** Asks the server to reload accounts, categories, tags and budgets from Firefly III. */
export function refreshReference(): Promise<ReferenceData> {
  // The server accepts state-changing requests only as application/json, even without a payload.
  return request<ReferenceData>("/api/entry/reference/refresh", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: "{}",
  });
}

export async function fetchDescriptionSuggestions(query: string, signal: AbortSignal): Promise<string[]> {
  const params = new URLSearchParams({ q: query });
  const body = await request<{ descriptions: string[] }>(`/api/entry/suggest/descriptions?${params.toString()}`, {
    signal,
  });
  return body.descriptions;
}

export function submitRows(rows: SubmitRow[]): Promise<SubmitResponse> {
  return request<SubmitResponse>("/api/entry/submit", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ rows }),
  });
}
