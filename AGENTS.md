# B3News — Agent Guide

F# / .NET console + minimal API. Single app project + xunit test project.

## Build / Run / Test
- SDK: .NET 10 — `B3News.fsproj:5` targets `net10.0`; trust the fsproj.
- `dotnet build`
- `dotnet run -- <command>` — note `--` to forward args (do not pass `--nologo` to `dotnet run`; it leaks into app args):
  - `update <data1> [<data2>]` (dates `YYYY-MM-DD`, validated via `DateTime.TryParseExact` in `Program.fs`; missing arg defaults to today)
  - `server` (Kestrel `http://localhost:8082` from `b3news.settings.json`)
- Tests: `dotnet test`
- Single test: `dotnet test B3News.Tests --filter "FullyQualifiedName~<TestName>"`
- Do NOT trust `b3news.service:11` (`B3News daemon`) — no `daemon` command exists in `Program.fs`; stale deploy file.

## What to read first
- `CONTEXT.md` — normative `Source | Kind | FetchErr | SaveOutcome` glossary. Don't duplicate; follow it.
- `Program.fs` (CLI entry), `Api.fs:263` (routes), `Config.fs` (settings), `Download.fs:81,185` (classify/fetch), `JsonStore.fs:25` (merge/dedupe).
- `docs/plans/<YYYY-MM-DD-name>/` — implementation plans, one numbered step file per change. Write a plan there before non-trivial work.

## Gotchas agents will miss
1. **F# file order is load order.** `B3News.fsproj:13-25` lists compile order (`Log → … → NewsSummary → Api → Program`). Insert new `.fs` files in dependency position, not alphabetically.
2. **All paths are CWD-relative.** `Config.fs:52` + `reportsJson="reports.json"` resolve via `Directory.GetCurrentDirectory()`. Run from repo root. `b3news.settings.json` and `reports.json` are gitignored — missing file = empty tickers / empty list, not an error.
3. **`Download.classify` is the only URL router** (`Fnet | Enet | EnetNbc | Unknown`). Never re-regex URLs; pattern-match `Source`. Enet flow needs viewer-GET warmup + POST to `/ENETWEB/.../ExibirPDF` with 3x SSL-only retry; `EnetNbc`/`Unknown` → `BadUrl`.
4. **`FetchErr` bodies truncated to 256 chars** at boundary (`Download.fs:99`); use `describeFetchErr` / `httpStatusForFetchErr` for logs and `/pdf/{id}.pdf` responses. `detectKind` defaults to `Pdf` unless `<HTML>` found.
5. **Snapshot pagination:** `Api.parseQuery` + `DetailsQuery.withClientRecordCount`. Unless `updateRecords=true`, server truncates to `clientRecordCount` before sort/paginate. `/api/news/count` is `IMemoryCache` + `PhysicalFileProvider.Watch(reports.json)` with 10-min sliding expiry.
6. **Per-item summaries:** `NewsDetail.summary` (`""`/null = not summarized yet) is cached in `reports.json` itself — no TTL, published text never changes. `GET /api/news/{id}/summary` returns cached `{summary}` immediately; otherwise it starts an in-memory, one-per-id background job and returns `{status:"fetching"|"summarizing", message}`; without a configured LLM key an uncached GET replies 500 and starts nothing; both viewers poll until `{summary}` or an error. Steps come from `NewsSummary.summarizeDetail`'s `onStep` (extract → LLM). Job state (`Api.SummaryState`) is per-process, lost on restart; persist failure serves the summary once from memory (`Succeeded`) then removes the entry; a failed job replies the error once (`Failed of SummaryErr`, 502 generation / 500 store) and the next GET restarts generation (no retry query param). `/no_think` prefix gated by `DisableReasoning` (Nemotron-only, `NewsSummary.fs:8`); `SummarySettings` is just `Model|MaxChars|MaxRetries|DisableReasoning`. `updateSummary` serializes in-process writes with `writeLock`; `updateNewsCust` preserves cached summaries for re-fetched keys. Frontend: chevron + modal + poll loop in both viewers; `report_viewer_legacy.html` must stay iOS 9-safe (XHR, no `fetch`/`Promise`/`classList`/flex — see passo 4).
7. **Tests:** `IntegrationTests.fs` boots a real server on `:8082`, writes `reports.json` (+`.bak` restore), sleeps 5s+1s — slow/flaky, fails if `:8082` busy. Unit suites (`*Tests.fs`) are safe; `ApiTests.fs` handler tests + `JsonStoreSummaryTests.fs` use temp dirs/files; live generation / live B3/CVM paths need network + `NVIDIA_API_KEY=nvapi-...`.
