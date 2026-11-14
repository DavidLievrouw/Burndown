// Local date-times of the entry grid are plain `yyyy-MM-ddTHH:mm` text in the configured zone. They are never turned
// into a browser Date in the local zone: arithmetic goes through UTC so the browser's zone and DST never interfere.

const WHEN = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$/;

export const WHEN_HINT = "Use a date and/or time such as 5, 5/3, 5/3/2026, 14:30 or 5/3 14:30";

function pad(value: number, length = 2): string {
  return String(value).padStart(length, "0");
}

/** Builds `yyyy-MM-ddTHH:mm` from its parts, without checking them. */
export function buildWhen(year: number, month: number, day: number, hour: number, minute: number): string {
  return `${pad(year, 4)}-${pad(month)}-${pad(day)}T${pad(hour)}:${pad(minute)}`;
}

function numbers(match: RegExpExecArray): [number, number, number, number, number] {
  return match.slice(1).map(Number) as [number, number, number, number, number];
}

/** Splits a valid `yyyy-MM-ddTHH:mm` into `[year, month, day, hour, minute]`. */
export function splitWhen(when: string): [number, number, number, number, number] {
  const match = WHEN.exec(when);
  if (!match) {
    throw new Error(`Not a local date-time: ${when}`);
  }
  return numbers(match);
}

/** The date part (`yyyy-MM-dd`) of a local date-time. */
export function whenDate(when: string): string {
  return when.slice(0, 10);
}

/** Whether `text` is a real `yyyy-MM-ddTHH:mm` date-time. */
export function isWhen(text: string): boolean {
  const match = WHEN.exec(text);
  if (!match) {
    return false;
  }
  const [year, month, day, hour, minute] = numbers(match);
  const date = new Date(Date.UTC(year, month - 1, day));
  return (
    date.getUTCFullYear() === year &&
    date.getUTCMonth() === month - 1 &&
    date.getUTCDate() === day &&
    hour < 24 &&
    minute < 60
  );
}

/** Adds minutes in plain wall-clock arithmetic (no DST), for example 23:59 + 1 = 00:00 on the next date. */
export function addMinutes(when: string, minutes: number): string {
  const match = WHEN.exec(when);
  if (!match) {
    throw new Error(`Not a local date-time: ${when}`);
  }
  const [year, month, day, hour, minute] = numbers(match);
  const moved = new Date(Date.UTC(year, month - 1, day, hour, minute + minutes));
  return buildWhen(
    moved.getUTCFullYear(),
    moved.getUTCMonth() + 1,
    moved.getUTCDate(),
    moved.getUTCHours(),
    moved.getUTCMinutes(),
  );
}

/** Takes the local date-time out of the server's "now" (an ISO timestamp with offset), minute precision. */
export function whenFromServerNow(now: string): string {
  const local = now.slice(0, 16);
  if (!isWhen(local)) {
    throw new Error(`Unexpected server time: ${now}`);
  }
  return local;
}

/** Formats `2026-03-05T14:30` as `05/03/2026 14:30`. */
export function formatWhen(when: string): string {
  const match = WHEN.exec(when);
  if (!match) {
    return when;
  }
  return `${match[3]}/${match[2]}/${match[1]} ${match[4]}:${match[5]}`;
}

export type WhenParse = { ok: true; when: string } | { ok: false; message: string };

const TIME_TOKEN = /^(\d{1,2}):(\d{2})$/;

function parseDateToken(token: string, current: string): string | null {
  const parts = token.split(/[/.-]/);
  if (parts.length > 3 || parts.some((part) => !/^\d{1,4}$/.test(part))) {
    return null;
  }
  const [dayText, monthText, yearText] = parts;
  const base = WHEN.exec(current);
  if (!base || dayText === undefined) {
    return null;
  }
  const day = Number(dayText);
  const month = monthText === undefined ? Number(base[2]) : Number(monthText);
  let year = yearText === undefined ? Number(base[1]) : Number(yearText);
  if (yearText !== undefined) {
    if (yearText.length === 2) {
      year += 2000;
    } else if (yearText.length !== 4) {
      return null;
    }
  }
  const check = new Date(Date.UTC(year, month - 1, day));
  if (check.getUTCFullYear() !== year || check.getUTCMonth() !== month - 1 || check.getUTCDate() !== day) {
    return null;
  }
  return `${pad(year, 4)}-${pad(month)}-${pad(day)}`;
}

/**
 * Reads what the user typed in the date-time field. Missing parts come from `current`: typing only a date keeps the
 * time of day, typing only a time keeps the date, and a short date (`5` or `5/3`) takes the rest from `current`.
 */
export function parseWhenInput(text: string, current: string): WhenParse {
  const fail = (): WhenParse => ({ ok: false, message: WHEN_HINT });
  const tokens = text.trim().split(/\s+/).filter(Boolean);
  if (tokens.length === 0 || tokens.length > 2) {
    return fail();
  }

  let date = whenDate(current);
  let time = current.slice(11, 16);
  let dateSeen = false;
  let timeSeen = false;
  for (const token of tokens) {
    const timeMatch = TIME_TOKEN.exec(token);
    if (timeMatch) {
      const hour = Number(timeMatch[1]);
      const minute = Number(timeMatch[2]);
      if (timeSeen || hour > 23 || minute > 59) {
        return fail();
      }
      time = `${pad(hour)}:${pad(minute)}`;
      timeSeen = true;
    } else {
      const parsed = dateSeen ? null : parseDateToken(token, current);
      if (parsed === null) {
        return fail();
      }
      date = parsed;
      dateSeen = true;
    }
  }
  return { ok: true, when: `${date}T${time}` };
}
