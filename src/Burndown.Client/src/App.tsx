import { useEffect, useState } from "react";
import { EntryPage } from "./features/entry/EntryPage";
import { OverviewPage } from "./features/overview/OverviewPage";

type Page = "overview" | "entry";

function pageFromHash(): Page {
  return window.location.hash.startsWith("#/entry") ? "entry" : "overview";
}

function usePage(): [Page, (page: Page) => void] {
  const [page, setPage] = useState<Page>(pageFromHash);
  useEffect(() => {
    const onHashChange = () => setPage(pageFromHash());
    window.addEventListener("hashchange", onHashChange);
    return () => window.removeEventListener("hashchange", onHashChange);
  }, []);
  return [
    page,
    (next) => {
      window.location.hash = next === "entry" ? "#/entry" : "#/";
    },
  ];
}

const LINK_CLASS = "rounded-md px-3 py-1.5 text-sm font-medium";

export function App() {
  const [page, navigate] = usePage();
  // Entry stays mounted once opened, so typed rows are not lost when the owner peeks at the overview.
  const [entryOpened, setEntryOpened] = useState(page === "entry");
  if (page === "entry" && !entryOpened) {
    setEntryOpened(true);
  }

  return (
    <>
      <nav aria-label="Main" className="border-b border-border bg-surface-raised">
        <div className="mx-auto flex max-w-[1600px] items-center gap-2 px-6 py-2">
          {(["overview", "entry"] as const).map((target) => (
            <a
              key={target}
              href={target === "entry" ? "#/entry" : "#/"}
              aria-current={page === target ? "page" : undefined}
              onClick={(event) => {
                event.preventDefault();
                navigate(target);
              }}
              className={`${LINK_CLASS} ${page === target ? "bg-surface text-text" : "text-muted hover:text-text"}`}
            >
              {target === "entry" ? "Entry" : "Overview"}
            </a>
          ))}
        </div>
      </nav>
      {page === "overview" ? <OverviewPage /> : null}
      {entryOpened ? (
        <div hidden={page !== "entry"}>
          <EntryPage />
        </div>
      ) : null}
    </>
  );
}
