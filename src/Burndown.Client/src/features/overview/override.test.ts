import { describe, expect, it } from "vitest";
import { parseOverride } from "./override";

describe("parseOverride", () => {
  it("treats blank input as no override", () => {
    expect(parseOverride("")).toEqual({ kind: "empty" });
    expect(parseOverride("   ")).toEqual({ kind: "empty" });
  });

  it.each([
    ["1500", "1500"],
    ["1500,5", "1500.5"],
    ["1500.50", "1500.50"],
    [" 0,05 ", "0.05"],
    ["0", "0"],
    ["999999999", "999999999"],
  ])("accepts %s as %s", (text, value) => {
    expect(parseOverride(text)).toEqual({ kind: "valid", value });
  });

  it.each(["abc", "1.234,56", "1,2,3", "12,", ",5", "1500,555", "1e3", "1 500", "1000000000"])("rejects %s", (text) => {
    expect(parseOverride(text).kind).toBe("invalid");
  });

  it("explains a negative amount", () => {
    expect(parseOverride("-5")).toEqual({ kind: "invalid", message: "The budget cannot be negative." });
  });
});
