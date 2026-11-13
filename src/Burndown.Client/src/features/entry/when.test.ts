import { describe, expect, it } from "vitest";
import { addMinutes, formatWhen, isWhen, parseWhenInput, whenFromServerNow } from "./when";

const CURRENT = "2026-03-05T14:30";

describe("addMinutes", () => {
  it.each([
    ["2026-03-05T14:30", 1, "2026-03-05T14:31"],
    ["2026-03-05T23:59", 1, "2026-03-06T00:00"],
    ["2026-02-28T23:59", 1, "2026-03-01T00:00"],
    ["2028-02-28T23:59", 1, "2028-02-29T00:00"],
    ["2026-12-31T23:59", 1, "2027-01-01T00:00"],
    ["2026-03-05T00:00", -1, "2026-03-04T23:59"],
  ])("%s + %i = %s", (when, minutes, expected) => {
    expect(addMinutes(when, minutes)).toBe(expected);
  });
});

describe("whenFromServerNow", () => {
  it("keeps the local wall-clock time of the zone, not the browser's", () => {
    expect(whenFromServerNow("2026-03-05T23:50:12.123+01:00")).toBe("2026-03-05T23:50");
  });
});

describe("isWhen / formatWhen", () => {
  it("validates real dates and times", () => {
    expect(isWhen("2026-03-05T14:30")).toBe(true);
    expect(isWhen("2026-02-30T14:30")).toBe(false);
    expect(isWhen("2026-03-05T24:00")).toBe(false);
    expect(isWhen("nonsense")).toBe(false);
  });

  it("formats as dd/MM/yyyy HH:mm", () => {
    expect(formatWhen("2026-03-05T14:30")).toBe("05/03/2026 14:30");
  });
});

describe("parseWhenInput", () => {
  it.each([
    ["5", "2026-03-05T14:30"],
    ["7", "2026-03-07T14:30"],
    ["7/4", "2026-04-07T14:30"],
    ["7.4", "2026-04-07T14:30"],
    ["7-4-2025", "2025-04-07T14:30"],
    ["07/04/25", "2025-04-07T14:30"],
    ["09:05", "2026-03-05T09:05"],
    ["9:05", "2026-03-05T09:05"],
    ["7/4 09:05", "2026-04-07T09:05"],
    ["09:05 7/4", "2026-04-07T09:05"],
    ["  05/03/2026   14:31 ", "2026-03-05T14:31"],
  ])("reads %j", (text, expected) => {
    expect(parseWhenInput(text, CURRENT)).toEqual({ ok: true, when: expected });
  });

  it.each(["", "  ", "abc", "32", "30/2", "5/13", "25:00", "12:60", "1/2/3/4", "5/3/202", "9:05 10:10", "5 6"])(
    "rejects %j",
    (text) => {
      expect(parseWhenInput(text, CURRENT).ok).toBe(false);
    },
  );
});
