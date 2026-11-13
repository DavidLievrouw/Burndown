import { ApiError } from "./api";

interface Described {
  title: string;
  hint: string;
}

/** Maps a failure to a user-readable explanation; the server's `detail` is shown underneath. */
export function describeError(error: unknown, fallbackTitle = "The overview could not be loaded"): Described {
  const code = error instanceof ApiError ? error.code : "unknown";
  switch (code) {
    case "firefly.unauthorized":
    case "firefly.forbidden":
      return {
        title: "Firefly III rejected the access token",
        hint: "The token is invalid or expired. Create a new personal access token in Firefly III (Profile → OAuth → Personal Access Tokens) and restart Burndown with it.",
      };
    case "firefly.unavailable":
      return {
        title: "Firefly III is not reachable",
        hint: "Check that Firefly III is running and that Firefly__BaseUrl points to it, then try again.",
      };
    case "firefly.timeout":
      return { title: "Firefly III took too long to answer", hint: "Try again in a moment." };
    case "network":
      return { title: "The Burndown server is not reachable", hint: "Check that the Burndown container is running." };
    default:
      return { title: fallbackTitle, hint: "Try again. If it keeps failing, check the server logs." };
  }
}

export function ErrorPanel({
  error,
  onRetry,
  fallbackTitle,
}: {
  error: unknown;
  onRetry: () => void;
  fallbackTitle?: string;
}) {
  const { title, hint } = describeError(error, fallbackTitle);
  const detail = error instanceof Error ? error.message : null;
  return (
    <div role="alert" className="rounded-lg border border-bad/50 bg-bad/10 p-6">
      <h2 className="text-lg font-semibold text-bad">{title}</h2>
      <p className="mt-2 text-sm">{hint}</p>
      {detail ? <p className="mt-2 text-sm text-muted">{detail}</p> : null}
      <button
        type="button"
        onClick={onRetry}
        className="mt-4 rounded-md border border-border bg-surface-raised px-3 py-1.5 text-sm hover:border-accent"
      >
        Try again
      </button>
    </div>
  );
}
