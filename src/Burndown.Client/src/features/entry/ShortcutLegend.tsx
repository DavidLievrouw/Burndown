const SHORTCUTS: readonly { keys: string; action: string }[] = [
  { keys: "Tab · Shift+Tab", action: "Next / previous field; Tab after the last field of the last row adds a row" },
  { keys: "Enter", action: "Accept the suggestion and go to the next field; after the last field, to the next row" },
  { keys: "Ctrl+Enter", action: "Submit all valid rows" },
  { keys: "Ctrl+↑ · Ctrl+↓", action: "Same field, row above / below" },
  { keys: "Alt+↑ · Alt+↓", action: "Move the row up / down" },
  { keys: "Ctrl+D", action: "Duplicate the row" },
  { keys: "Alt+Delete", action: "Delete the row" },
  { keys: "Esc", action: "Close the list / undo the typed text" },
];

export function ShortcutLegend() {
  return (
    <section aria-label="Keyboard shortcuts" className="mt-6 rounded-lg border border-border bg-surface-raised p-4">
      <h2 className="mb-3 text-sm font-semibold text-muted">Keyboard shortcuts</h2>
      <dl className="grid gap-x-8 gap-y-1.5 text-sm md:grid-cols-2">
        {SHORTCUTS.map((shortcut) => (
          <div key={shortcut.keys} className="flex gap-3">
            <dt className="w-40 shrink-0">
              <kbd className="rounded border border-border bg-surface px-1.5 py-0.5 font-mono text-xs">
                {shortcut.keys}
              </kbd>
            </dt>
            <dd className="text-muted">{shortcut.action}</dd>
          </div>
        ))}
      </dl>
      <p className="mt-3 text-xs text-muted">
        Type: <kbd className="font-mono">e</kbd>, <kbd className="font-mono">i</kbd> or{" "}
        <kbd className="font-mono">t</kbd> then Tab. Amounts accept a comma or a point. In the date and time, the day is
        selected first: type digits to replace a part (it moves on by itself), <kbd className="font-mono">←</kbd>{" "}
        <kbd className="font-mono">→</kbd> pick another part and <kbd className="font-mono">↑</kbd>{" "}
        <kbd className="font-mono">↓</kbd> change it.
      </p>
    </section>
  );
}
