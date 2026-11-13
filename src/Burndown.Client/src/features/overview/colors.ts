import type { PerDayTone } from "./api";

// The server returns intensities (the old app's alphas); the colours themselves are tuned for the dark theme.
// Green is capped at half intensity so the light text on top keeps a contrast of at least 4.5:1.
const GREEN = "76 195 138";
const RED = "239 107 107";
const GREEN_MAX_ALPHA = 0.5;

/** Background of the per-day cell, or `undefined` when it has no colour. */
export function perDayBackground(tone: PerDayTone, alpha: number): string | undefined {
  if (tone === "none" || alpha <= 0) {
    return undefined;
  }
  return tone === "green"
    ? `rgb(${GREEN} / ${(alpha * GREEN_MAX_ALPHA).toFixed(3)})`
    : `rgb(${RED} / ${alpha.toFixed(3)})`;
}

/** Background of the amount cell, or `undefined` when it has no colour. */
export function amountBackground(alpha: number): string | undefined {
  return alpha > 0 ? `rgb(${RED} / ${alpha.toFixed(3)})` : undefined;
}
