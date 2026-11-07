import { useQuery } from "@tanstack/react-query";

async function fetchLiveness(): Promise<boolean> {
  const response = await fetch("/health/live");
  return response.ok;
}

export function App() {
  const liveness = useQuery({ queryKey: ["health", "live"], queryFn: fetchLiveness });

  const status = liveness.isPending ? "Checking…" : liveness.data ? "Connected" : "Unavailable";

  return (
    <main className="mx-auto max-w-5xl p-8">
      <h1 className="text-2xl font-semibold">Burndown</h1>
      <p className="mt-2 text-muted">Keyboard-first entry and monthly overview for Firefly III.</p>
      <p className="mt-6" role="status">
        Server: <span className={liveness.data ? "text-ok" : "text-muted"}>{status}</span>
      </p>
    </main>
  );
}
