import "@testing-library/jest-dom/vitest";

// jsdom has no ResizeObserver; Recharts' responsive container needs one.
class ResizeObserverStub {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
}
globalThis.ResizeObserver ??= ResizeObserverStub;
