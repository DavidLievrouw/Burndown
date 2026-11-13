import { describe, expect, it } from "vitest";
import { amountBackground, perDayBackground } from "./colors";

describe("perDayBackground", () => {
  it("has no colour for the none tone or a zero intensity", () => {
    expect(perDayBackground("none", 0.4)).toBeUndefined();
    expect(perDayBackground("green", 0)).toBeUndefined();
    expect(perDayBackground("red", 0)).toBeUndefined();
  });

  it("caps green at half intensity to keep the text readable", () => {
    expect(perDayBackground("green", 1)).toBe("rgb(76 195 138 / 0.500)");
    expect(perDayBackground("green", 0.2)).toBe("rgb(76 195 138 / 0.100)");
  });

  it("uses the server intensity for red", () => {
    expect(perDayBackground("red", 0.35)).toBe("rgb(239 107 107 / 0.350)");
  });
});

describe("amountBackground", () => {
  it("is red scaled by the intensity, and absent at zero", () => {
    expect(amountBackground(0.25)).toBe("rgb(239 107 107 / 0.250)");
    expect(amountBackground(0)).toBeUndefined();
  });
});
