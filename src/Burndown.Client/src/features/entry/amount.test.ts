import { describe, expect, it } from "vitest";
import { formatAmountInput, parseAmount } from "./amount";

describe("parseAmount", () => {
  it.each([
    ["12", 1200],
    ["12,5", 1250],
    ["12.50", 1250],
    ["0,05", 5],
    ["7,", 700],
    [" 999999999,99 ", 99999999999],
  ])("reads %s", (text, cents) => {
    expect(parseAmount(text)).toEqual({ ok: true, cents });
  });

  it.each(["", "   ", "abc", "1,2,3", "1.234,56", "-5", "12,345", "0", "0,00", "1000000000", ",5", "12 50"])(
    "rejects %j",
    (text) => {
      expect(parseAmount(text).ok).toBe(false);
    },
  );

  it("does not suffer from floating point error", () => {
    expect(parseAmount("0,29")).toEqual({ ok: true, cents: 29 });
    expect(parseAmount("1,15")).toEqual({ ok: true, cents: 115 });
  });
});

describe("formatAmountInput", () => {
  it("formats cents with a comma and two decimals", () => {
    expect(formatAmountInput(1250)).toBe("12,50");
    expect(formatAmountInput(5)).toBe("0,05");
    expect(formatAmountInput(99999999999)).toBe("999999999,99");
  });
});
