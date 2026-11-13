export const MAX_AMOUNT = 999_999_999.99;

export type AmountParse = { ok: true; cents: number } | { ok: false; message: string };

/** Reads an amount typed with `,` or `.` as the decimal separator. Thousands separators are not accepted. */
export function parseAmount(text: string): AmountParse {
  const trimmed = text.trim();
  if (trimmed === "") {
    return { ok: false, message: "Enter an amount." };
  }
  const match = /^(\d+)(?:[.,](\d*))?$/.exec(trimmed);
  if (!match) {
    return { ok: false, message: "Use digits with , or . as the decimal separator, for example 12,50." };
  }
  const fraction = match[2] ?? "";
  if (fraction.length > 2) {
    return { ok: false, message: "An amount can have at most 2 decimals." };
  }
  const cents = Number(match[1]) * 100 + Number(fraction.padEnd(2, "0"));
  if (cents <= 0) {
    return { ok: false, message: "The amount must be greater than zero." };
  }
  if (cents > Math.round(MAX_AMOUNT * 100)) {
    return { ok: false, message: "That amount is too large." };
  }
  return { ok: true, cents };
}

/** Formats cents as `12,50`, the spelling the amount field normalizes to (no grouping, so it parses back). */
export function formatAmountInput(cents: number): string {
  const whole = Math.floor(cents / 100);
  return `${whole},${String(cents % 100).padStart(2, "0")}`;
}

/** The amount in euros for the JSON payload. */
export function centsToAmount(cents: number): number {
  return cents / 100;
}
