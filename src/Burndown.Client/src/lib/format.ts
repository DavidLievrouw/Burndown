// The single place that turns numbers and dates into text (PLAN D6): numbers and numeric dates follow nl-BE,
// day and month names are English. Nothing here depends on the browser's ambient locale.

const MONTH_NAMES = [
  "January",
  "February",
  "March",
  "April",
  "May",
  "June",
  "July",
  "August",
  "September",
  "October",
  "November",
  "December",
] as const;

const DAY_NAMES = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"] as const;

const NON_BREAKING_SPACE = "\u00a0";

function groupThousands(digits: string): string {
  return digits.replace(/\B(?=(\d{3})+(?!\d))/g, ".");
}

/** Formats an amount as `1.234,56`, rounded half away from zero to 2 decimals. */
export function formatNumber(value: number): string {
  const cents = Math.round(Math.abs(value) * 100);
  const whole = Math.floor(cents / 100);
  const fraction = String(cents % 100).padStart(2, "0");
  const sign = cents !== 0 && value < 0 ? "-" : "";
  return `${sign}${groupThousands(String(whole))},${fraction}`;
}

/** Formats an amount as `€ 1.234,56`; a negative amount reads `€ -1.234,56`. */
export function formatMoney(value: number): string {
  return `€${NON_BREAKING_SPACE}${formatNumber(value)}`;
}

/** Formats an amount without decimals for chart axes, for example `€ 1.250`. */
export function formatMoneyWhole(value: number): string {
  const rounded = Math.round(Math.abs(value));
  const sign = rounded !== 0 && value < 0 ? "-" : "";
  return `€${NON_BREAKING_SPACE}${sign}${groupThousands(String(rounded))}`;
}

function parseIsoDate(iso: string): { year: number; month: number; day: number } {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso);
  if (!match) {
    throw new Error(`Not an ISO date: ${iso}`);
  }
  return { year: Number(match[1]), month: Number(match[2]), day: Number(match[3]) };
}

/** Formats `2024-02-05` as `05/02/2024`. */
export function formatDate(iso: string): string {
  const { year, month, day } = parseIsoDate(iso);
  return `${String(day).padStart(2, "0")}/${String(month).padStart(2, "0")}/${year}`;
}

/** The three-letter English day name of `2024-02-05`, for example `Mon`. */
export function formatWeekday(iso: string): string {
  const { year, month, day } = parseIsoDate(iso);
  return DAY_NAMES[new Date(Date.UTC(year, month - 1, day)).getUTCDay()] ?? "";
}

/** Formats `2024-02-05` as `Mon 5 Feb`. */
export function formatDayLabel(iso: string): string {
  const { month, day } = parseIsoDate(iso);
  return `${formatWeekday(iso)} ${day} ${(MONTH_NAMES[month - 1] ?? "").slice(0, 3)}`;
}

/** Formats `2024-02` as `Feb`. */
export function formatMonthShort(month: string): string {
  const match = /^(\d{4})-(\d{2})$/.exec(month);
  if (!match) {
    throw new Error(`Not a month: ${month}`);
  }
  return (MONTH_NAMES[Number(match[2]) - 1] ?? "").slice(0, 3);
}

/** Formats `2024-02` as `February 2024`. */
export function formatMonth(month: string): string {
  const match = /^(\d{4})-(\d{2})$/.exec(month);
  if (!match) {
    throw new Error(`Not a month: ${month}`);
  }
  return `${MONTH_NAMES[Number(match[2]) - 1] ?? ""} ${match[1]}`;
}
