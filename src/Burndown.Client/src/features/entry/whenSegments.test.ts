import { describe, expect, it } from "vitest";
import {
  displayWhen,
  finishSegment,
  neighbourSegment,
  segmentAt,
  setSegment,
  stepSegment,
  typeDigit,
} from "./whenSegments";

const base = "2026-03-05T14:40";

describe("segmentAt", () => {
  it.each([
    [0, "day"],
    [2, "day"],
    [3, "month"],
    [5, "month"],
    [6, "year"],
    [10, "year"],
    [11, "hour"],
    [13, "hour"],
    [14, "minute"],
    [16, "minute"],
  ])("maps caret %i to %s", (caret, segment) => {
    expect(segmentAt(caret)).toBe(segment);
  });
});

describe("neighbourSegment", () => {
  it("moves and stops at both ends", () => {
    expect(neighbourSegment("day", 1)).toBe("month");
    expect(neighbourSegment("day", -1)).toBe("day");
    expect(neighbourSegment("minute", 1)).toBe("minute");
    expect(neighbourSegment("hour", -1)).toBe("year");
  });
});

describe("setSegment", () => {
  it("clamps the day to the new month", () => {
    expect(setSegment("2026-01-31T10:00", "month", 2)).toBe("2026-02-28T10:00");
  });

  it("clamps 29 February when the year stops being a leap year", () => {
    expect(setSegment("2024-02-29T10:00", "year", 2025)).toBe("2025-02-28T10:00");
  });
});

describe("stepSegment", () => {
  it("steps the day inside the month and wraps", () => {
    expect(stepSegment(base, "day", 1)).toBe("2026-03-06T14:40");
    expect(stepSegment("2026-03-31T14:40", "day", 1)).toBe("2026-03-01T14:40");
    expect(stepSegment("2026-03-01T14:40", "day", -1)).toBe("2026-03-31T14:40");
    expect(stepSegment("2026-02-01T14:40", "day", -1)).toBe("2026-02-28T14:40");
  });

  it("wraps month, hour and minute", () => {
    expect(stepSegment("2026-12-05T14:40", "month", 1)).toBe("2026-01-05T14:40");
    expect(stepSegment("2026-03-05T23:40", "hour", 1)).toBe("2026-03-05T00:40");
    expect(stepSegment("2026-03-05T14:00", "minute", -1)).toBe("2026-03-05T14:59");
  });

  it("clamps the day when the month changes", () => {
    expect(stepSegment("2026-01-31T10:00", "month", 1)).toBe("2026-02-28T10:00");
  });

  it("steps the year within bounds", () => {
    expect(stepSegment(base, "year", 1)).toBe("2027-03-05T14:40");
    expect(stepSegment("9999-03-05T14:40", "year", 1)).toBe("9999-03-05T14:40");
  });
});

describe("typeDigit", () => {
  it("applies a first digit that cannot grow and advances", () => {
    const result = typeDigit(base, "day", "", "5");
    expect(result).toEqual({ when: "2026-03-05T14:40", buffer: "", advance: true });
    expect(typeDigit(base, "day", "", "4")).toMatchObject({ when: "2026-03-04T14:40", advance: true });
  });

  it("applies a first digit that can still grow and waits for a second one", () => {
    const first = typeDigit(base, "day", "", "1");
    expect(first).toEqual({ when: "2026-03-01T14:40", buffer: "1", advance: false });
    const second = typeDigit(first.when, "day", first.buffer, "7");
    expect(second).toEqual({ when: "2026-03-17T14:40", buffer: "", advance: true });
  });

  it("waits after a leading zero without changing the value", () => {
    const first = typeDigit(base, "day", "", "0");
    expect(first).toEqual({ when: base, buffer: "0", advance: false });
    expect(typeDigit(first.when, "day", first.buffer, "9")).toMatchObject({
      when: "2026-03-09T14:40",
      advance: true,
    });
  });

  it("respects the days of the month", () => {
    expect(typeDigit("2026-02-10T10:00", "day", "", "3")).toMatchObject({ when: "2026-02-03T10:00", advance: true });
    expect(typeDigit("2024-02-10T10:00", "day", "2", "9")).toMatchObject({ when: "2024-02-29T10:00", advance: true });
    expect(typeDigit("2026-02-10T10:00", "day", "2", "9")).toMatchObject({ when: "2026-02-09T10:00", advance: true });
  });

  it("types the month, hour and minute", () => {
    expect(typeDigit(base, "month", "1", "2")).toMatchObject({ when: "2026-12-05T14:40", advance: true });
    expect(typeDigit(base, "month", "", "2")).toMatchObject({ when: "2026-02-05T14:40", advance: true });
    expect(typeDigit(base, "hour", "2", "5")).toMatchObject({ when: "2026-03-05T05:40", advance: true });
    expect(typeDigit(base, "hour", "", "0")).toMatchObject({ buffer: "0", advance: false });
    expect(typeDigit(base, "minute", "", "7")).toMatchObject({ when: "2026-03-05T14:07", advance: true });
  });

  it("collects four year digits before applying", () => {
    let result = typeDigit(base, "year", "", "2");
    expect(result).toEqual({ when: base, buffer: "2", advance: false });
    result = typeDigit(result.when, "year", result.buffer, "0");
    result = typeDigit(result.when, "year", result.buffer, "2");
    expect(result.advance).toBe(false);
    result = typeDigit(result.when, "year", result.buffer, "4");
    expect(result).toEqual({ when: "2024-03-05T14:40", buffer: "", advance: true });
  });

  it("drops a year below 1000", () => {
    expect(typeDigit(base, "year", "002", "4")).toEqual({ when: base, buffer: "", advance: false });
  });
});

describe("finishSegment", () => {
  it("turns a two digit year into 20xx", () => {
    expect(finishSegment(base, "year", "27")).toBe("2027-03-05T14:40");
  });

  it("leaves everything else alone", () => {
    expect(finishSegment(base, "year", "2")).toBe(base);
    expect(finishSegment(base, "year", "202")).toBe(base);
    expect(finishSegment(base, "day", "1")).toBe(base);
  });
});

describe("displayWhen", () => {
  it("formats the value and shows a half typed year", () => {
    expect(displayWhen(base, "day", "")).toBe("05/03/2026 14:40");
    expect(displayWhen(base, "year", "20")).toBe("05/03/20__ 14:40");
  });
});
