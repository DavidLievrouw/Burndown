export type OverrideInput = { kind: "empty" } | { kind: "valid"; value: string } | { kind: "invalid"; message: string };

const MESSAGE = "Enter an amount such as 1500 or 1500,50.";

/**
 * Input affordance for the budget override: accepts `,` or `.` as the decimal separator and returns the invariant
 * text the server expects. The server stays authoritative.
 */
export function parseOverride(text: string): OverrideInput {
  const trimmed = text.trim();
  if (trimmed === "") {
    return { kind: "empty" };
  }
  if (trimmed.startsWith("-")) {
    return { kind: "invalid", message: "The budget cannot be negative." };
  }
  const match = /^(\d{1,9})(?:[.,](\d{1,2}))?$/.exec(trimmed);
  if (!match) {
    return { kind: "invalid", message: MESSAGE };
  }
  const whole = match[1] ?? "";
  return { kind: "valid", value: match[2] === undefined ? whole : `${whole}.${match[2]}` };
}
