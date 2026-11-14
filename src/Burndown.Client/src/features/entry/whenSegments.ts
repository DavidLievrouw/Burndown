import { buildWhen, formatWhen, splitWhen } from "./when";

// The When field shows `dd/MM/yyyy HH:mm` and edits one segment at a time, like a native date-time input: the
// segment is selected, digits replace it, the arrows move between segments or step the value.

export type Segment = "day" | "month" | "year" | "hour" | "minute";

export const SEGMENTS: readonly Segment[] = ["day", "month", "year", "hour", "minute"];

/** Character ranges of the segments in the displayed text `dd/MM/yyyy HH:mm` (end exclusive). */
export const SEGMENT_RANGES: Record<Segment, readonly [number, number]> = {
  day: [0, 2],
  month: [3, 5],
  year: [6, 10],
  hour: [11, 13],
  minute: [14, 16],
};

const MIN_YEAR = 1000;
const MAX_YEAR = 9999;

/** The segment a caret position belongs to; a caret on a separator belongs to the segment before it. */
export function segmentAt(caret: number): Segment {
  if (caret <= 2) {
    return "day";
  }
  if (caret <= 5) {
    return "month";
  }
  if (caret <= 10) {
    return "year";
  }
  return caret <= 13 ? "hour" : "minute";
}

export function neighbourSegment(segment: Segment, direction: 1 | -1): Segment {
  const index = SEGMENTS.indexOf(segment) + direction;
  return SEGMENTS[Math.min(Math.max(index, 0), SEGMENTS.length - 1)] ?? segment;
}

function daysInMonth(year: number, month: number): number {
  return new Date(Date.UTC(year, month, 0)).getUTCDate();
}

function limits(when: string, segment: Exclude<Segment, "year">): readonly [number, number] {
  switch (segment) {
    case "day": {
      const [year, month] = splitWhen(when);
      return [1, daysInMonth(year, month)];
    }
    case "month":
      return [1, 12];
    case "hour":
      return [0, 23];
    case "minute":
      return [0, 59];
  }
}

/** Sets one segment to a value that is valid for it; a day that no longer fits the month moves to its last day. */
export function setSegment(when: string, segment: Segment, value: number): string {
  let [year, month, day, hour, minute] = splitWhen(when);
  switch (segment) {
    case "day":
      day = value;
      break;
    case "month":
      month = value;
      break;
    case "year":
      year = value;
      break;
    case "hour":
      hour = value;
      break;
    case "minute":
      minute = value;
      break;
  }
  day = Math.min(day, daysInMonth(year, month));
  return buildWhen(year, month, day, hour, minute);
}

/** Moves one segment up or down by `delta`. Day, month, hour and minute wrap inside their own range, without carry. */
export function stepSegment(when: string, segment: Segment, delta: number): string {
  const [year, month, day, hour, minute] = splitWhen(when);
  if (segment === "year") {
    return setSegment(when, "year", Math.min(Math.max(year + delta, MIN_YEAR), MAX_YEAR));
  }
  const [min, max] = limits(when, segment);
  const current = { day, month, hour, minute }[segment];
  const size = max - min + 1;
  const next = ((((current - min + delta) % size) + size) % size) + min;
  return setSegment(when, segment, next);
}

export interface TypeResult {
  when: string;
  /** The digits typed into the segment so far, to combine with the next digit. */
  buffer: string;
  /** Whether the segment is complete and the next one should be selected. */
  advance: boolean;
}

/**
 * Applies one typed digit to a segment. A digit that cannot grow into a valid two-digit value (a day starting with 4,
 * a month with 2) completes the segment at once; otherwise the segment waits for the second digit. The year only
 * takes effect once all four digits are typed.
 */
export function typeDigit(when: string, segment: Segment, buffer: string, digit: string): TypeResult {
  if (segment === "year") {
    const next = buffer + digit;
    if (next.length < 4) {
      return { when, buffer: next, advance: false };
    }
    const year = Number(next);
    if (year < MIN_YEAR) {
      return { when, buffer: "", advance: false };
    }
    return { when: setSegment(when, "year", year), buffer: "", advance: true };
  }

  const [min, max] = limits(when, segment);
  let next = buffer + digit;
  if (next.length === 2) {
    const value = Number(next);
    if (value >= min && value <= max) {
      return { when: setSegment(when, segment, value), buffer: "", advance: true };
    }
    next = digit;
  }
  const value = Number(next);
  if (value * 10 > max) {
    return { when: setSegment(when, segment, value), buffer: "", advance: true };
  }
  if (value < min) {
    return { when, buffer: next, advance: false };
  }
  return { when: setSegment(when, segment, value), buffer: next, advance: false };
}

/** Finishes a half-typed year when the segment is left: two digits mean 20xx, anything else is dropped. */
export function finishSegment(when: string, segment: Segment, buffer: string): string {
  if (segment === "year" && buffer.length === 2) {
    return setSegment(when, "year", 2000 + Number(buffer));
  }
  return when;
}

/** The text to show: the formatted date-time, with a half-typed year shown as the digits typed so far (`20__`). */
export function displayWhen(when: string, segment: Segment, buffer: string): string {
  const text = formatWhen(when);
  if (segment !== "year" || buffer === "") {
    return text;
  }
  const [start, end] = SEGMENT_RANGES.year;
  return text.slice(0, start) + buffer.padEnd(end - start, "_") + text.slice(end);
}
