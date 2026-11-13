import { afterEach, describe, expect, it, vi } from "vitest";
import { refreshReference } from "./api";

describe("refreshReference", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("posts as application/json, which the server requires for every state-changing request", async () => {
    const fetchMock = vi.fn(
      async () => new Response("{}", { status: 200, headers: { "Content-Type": "application/json" } }),
    );
    vi.stubGlobal("fetch", fetchMock);

    await refreshReference();

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("/api/entry/reference/refresh");
    expect(init.method).toBe("POST");
    expect(new Headers(init.headers).get("Content-Type")).toBe("application/json");
  });
});
