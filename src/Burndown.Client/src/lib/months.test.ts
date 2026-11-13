import { describe, expect, it } from "vitest";
import { addMonths } from "./months";

describe("addMonths", () => {
  it.each([
    ["2024-01", 1, "2024-02"],
    ["2024-12", 1, "2025-01"],
    ["2024-01", -1, "2023-12"],
    ["2023-12", -12, "2022-12"],
    ["2024-05", 0, "2024-05"],
    ["2024-05", 20, "2026-01"],
  ])("%s %s months gives %s", (month, delta, expected) => {
    expect(addMonths(month, delta)).toBe(expected);
  });

  it("rejects malformed input", () => {
    expect(() => addMonths("2024-1", 1)).toThrow();
  });
});
