import { describe, expect, it } from "vitest";
import { containsAllWords, filterItems, normalize } from "./matching";

const items = [
  { key: "1", label: "Café de la Paix" },
  { key: "2", label: "Delhaize" },
  { key: "3", label: "Sparkasse Girokonto" },
  { key: "4", label: "Spaarrekening" },
];

describe("normalize", () => {
  it("ignores case and accents", () => {
    expect(normalize("CAFÉ")).toBe("cafe");
  });
});

describe("filterItems", () => {
  it("returns everything for a blank query, in the given order", () => {
    expect(filterItems(items, "  ").map((i) => i.key)).toEqual(["1", "2", "3", "4"]);
  });

  it("matches the start of any word, ignoring case and accents", () => {
    expect(filterItems(items, "cafe").map((i) => i.key)).toEqual(["1"]);
    expect(filterItems(items, "PAIX").map((i) => i.key)).toEqual(["1"]);
    expect(filterItems(items, "sp").map((i) => i.key)).toEqual(["3", "4"]);
  });

  it("requires every typed word to match", () => {
    expect(filterItems(items, "sp giro").map((i) => i.key)).toEqual(["3"]);
    expect(filterItems(items, "de pa").map((i) => i.key)).toEqual(["1"]);
  });

  it("falls back to a substring match when no word starts with the text", () => {
    expect(filterItems(items, "haize").map((i) => i.key)).toEqual(["2"]);
  });

  it("returns nothing when nothing matches", () => {
    expect(filterItems(items, "zzz")).toEqual([]);
  });
});

describe("containsAllWords", () => {
  it("checks that every typed word occurs in the label", () => {
    expect(containsAllWords("Bakery Janssens", "bak jans")).toBe(true);
    expect(containsAllWords("Bakery Janssens", "bak peeters")).toBe(false);
  });
});
