export interface ComboItem {
  key: string;
  label: string;
}

/** Lower-cases and removes accents, so `Café` and `cafe` compare equal. */
export function normalize(text: string): string {
  return text.normalize("NFD").replace(/\p{M}/gu, "").toLowerCase();
}

function words(text: string): string[] {
  return normalize(text)
    .split(/[^\p{L}\p{N}]+/u)
    .filter(Boolean);
}

/**
 * Filters items by what was typed, keeping their order (the server ranks them by last use). Every typed word must
 * start a word of the label; when nothing matches that way, it may occur anywhere in the label.
 */
export function filterItems<T extends ComboItem>(items: readonly T[], query: string): T[] {
  const typed = words(query);
  if (typed.length === 0) {
    return [...items];
  }
  const byWord = items.filter((item) => {
    const labelWords = words(item.label);
    return typed.every((token) => labelWords.some((word) => word.startsWith(token)));
  });
  if (byWord.length > 0) {
    return byWord;
  }
  return items.filter((item) => {
    const label = normalize(item.label);
    return typed.every((token) => label.includes(token));
  });
}

/** Whether the label contains every typed word (used to drop stale suggestions for an older query). */
export function containsAllWords(label: string, query: string): boolean {
  const text = normalize(label);
  return words(query).every((token) => text.includes(token));
}
