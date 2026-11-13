import { fireEvent, render } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { useMonthKeys } from "./useMonthKeys";

function Harness({ handlers }: { handlers: { onPrevious: () => void; onNext: () => void; onCurrent: () => void } }) {
  useMonthKeys(handlers);
  return (
    <div>
      <input aria-label="field" />
      <button type="button">button</button>
    </div>
  );
}

function setup() {
  const handlers = { onPrevious: vi.fn(), onNext: vi.fn(), onCurrent: vi.fn() };
  const view = render(<Harness handlers={handlers} />);
  return { handlers, ...view };
}

describe("useMonthKeys", () => {
  it("maps the arrows and Home to previous, next and current", () => {
    const { handlers } = setup();

    fireEvent.keyDown(window, { key: "ArrowLeft" });
    fireEvent.keyDown(window, { key: "ArrowRight" });
    fireEvent.keyDown(window, { key: "Home" });

    expect(handlers.onPrevious).toHaveBeenCalledTimes(1);
    expect(handlers.onNext).toHaveBeenCalledTimes(1);
    expect(handlers.onCurrent).toHaveBeenCalledTimes(1);
  });

  it("works while a button has focus", () => {
    const { handlers, getByText } = setup();

    fireEvent.keyDown(getByText("button"), { key: "ArrowRight" });

    expect(handlers.onNext).toHaveBeenCalledTimes(1);
  });

  it("leaves keys typed in a text field alone", () => {
    const { handlers, getByLabelText } = setup();

    fireEvent.keyDown(getByLabelText("field"), { key: "ArrowLeft" });
    fireEvent.keyDown(getByLabelText("field"), { key: "Home" });

    expect(handlers.onPrevious).not.toHaveBeenCalled();
    expect(handlers.onCurrent).not.toHaveBeenCalled();
  });

  it("ignores keys combined with a modifier", () => {
    const { handlers } = setup();

    fireEvent.keyDown(window, { key: "ArrowLeft", altKey: true });
    fireEvent.keyDown(window, { key: "ArrowRight", ctrlKey: true });
    fireEvent.keyDown(window, { key: "ArrowRight", shiftKey: true });

    expect(handlers.onPrevious).not.toHaveBeenCalled();
    expect(handlers.onNext).not.toHaveBeenCalled();
  });

  it("stops listening after unmount", () => {
    const { handlers, unmount } = setup();
    unmount();

    fireEvent.keyDown(window, { key: "ArrowLeft" });

    expect(handlers.onPrevious).not.toHaveBeenCalled();
  });
});
