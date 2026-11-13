import { describe, expect, it } from "vitest";
import {
  formatDate,
  formatDayLabel,
  formatMoney,
  formatMoneyWhole,
  formatMonth,
  formatMonthShort,
  formatNumber,
  formatWeekday,
} from "./format";

const nbsp = "\u00a0";

describe("formatNumber", () => {
  it.each([
    [0, "0,00"],
    [1234.5, "1.234,50"],
    [1234567.891, "1.234.567,89"],
    [999.995, "1.000,00"],
    [0.05, "0,05"],
    [-12.34, "-12,34"],
    [-1234.5, "-1.234,50"],
  ])("formats %s as %s", (value, expected) => {
    expect(formatNumber(value)).toBe(expected);
  });

  it("does not show a minus sign for a value that rounds to zero", () => {
    expect(formatNumber(-0.001)).toBe("0,00");
  });
});

describe("formatMoney", () => {
  it("uses a euro sign, a non-breaking space and nl-BE separators", () => {
    expect(formatMoney(1234.56)).toBe(`€${nbsp}1.234,56`);
  });

  it("puts the minus sign after the euro sign", () => {
    expect(formatMoney(-12.3)).toBe(`€${nbsp}-12,30`);
  });
});

describe("formatMoneyWhole", () => {
  it("drops the decimals for axis labels", () => {
    expect(formatMoneyWhole(1250.4)).toBe(`€${nbsp}1.250`);
    expect(formatMoneyWhole(-300)).toBe(`€${nbsp}-300`);
    expect(formatMoneyWhole(0)).toBe(`€${nbsp}0`);
  });
});

describe("dates", () => {
  it("formats an ISO date as dd/MM/yyyy", () => {
    expect(formatDate("2024-02-05")).toBe("05/02/2024");
  });

  it("names the weekday in English, independent of the locale", () => {
    expect(formatWeekday("2024-02-05")).toBe("Mon");
    expect(formatWeekday("2024-02-11")).toBe("Sun");
  });

  it("formats a day label", () => {
    expect(formatDayLabel("2024-02-05")).toBe("Mon 5 Feb");
  });

  it("formats a month in English", () => {
    expect(formatMonth("2023-12")).toBe("December 2023");
    expect(formatMonth("2026-01")).toBe("January 2026");
  });

  it("rejects malformed input", () => {
    expect(() => formatDate("05/02/2024")).toThrow();
    expect(() => formatMonth("2024-2")).toThrow();
  });
});

describe("formatMonthShort", () => {
  it("uses the fixed English short month name", () => {
    expect(formatMonthShort("2024-02")).toBe("Feb");
    expect(formatMonthShort("2023-12")).toBe("Dec");
  });
});
