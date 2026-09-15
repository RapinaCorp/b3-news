# B3News — Agent Guide

F# / .NET console + minimal API. Single app project + xunit test project.

## Build / Run / Test
- SDK: README says .NET 10 SDK, but `B3News.fsproj:5` targets `net9.0` — trust fsproj; `dotnet build` works with either.
- `dotnet build`
- `dotnet run -- <command>` — note `--` to forward args:
  - `update <data1> [<data2>]` (dates `YYYY-MM-DD`; missing arg defaults to today in `Program.fs:27`)
  - `server` (Kestrel `http://localhost:8082` from `b3news.settings.json`)
- Tests: `dotnet test`
- Single test: `dotnet test B3News.Tests --filter "FullyQualifiedName~<TestName>"`
- Do NOT trust `b3news.service:11` (`B3News daemon`) — no `daemon` command exists in `Program.fs:54`; stale deploy file.

## What to read first
- `CONTEXT.md` — normative `Source | Kind | FetchErr | SaveOutcome` glossary. Don't duplicate; follow it.
- `Program.fs` (CLI entry), `Api.fs:263` (routes), `Config.fs` (settings), `Download.fs:81,185` (classify/fetch), `JsonStore.fs:25` (merge/dedupe).

## Gotchas agents will miss
1. **F# file order is load order.** `B3News.fsproj:13-25` lists compile order (`Log → … → NewsSummary → Api → Program`). Insert new `.fs` files in dependency position, not alphabetically.
2. **All paths are CWD-relative.** `Config.fs:52` + `reportsJson="reports.json"` resolve via `Directory.GetCurrentDirectory()`. Run from repo root. `b3news.settings.json` and `reports.json` are gitignored — missing file = empty tickers / empty list, not an error.
3. **`Download.classify` is the only URL router** (`Fnet | Enet | EnetNbc | Unknown`). Never re-regex URLs; pattern-match `Source`. Enet flow needs viewer-GET warmup + POST to `/ENETWEB/.../ExibirPDF` with 3x SSL-only retry; `EnetNbc`/`Unknown` → `BadUrl`.
4. **`FetchErr` bodies truncated to 256 chars** at boundary (`Download.fs:99`); use `describeFetchErr` / `httpStatusForFetchErr` for logs and `/pdf/{id}.pdf` responses. `detectKind` defaults to `Pdf` unless `<HTML>` found.
5. **Snapshot pagination:** `Api.parseQuery` + `DetailsQuery.withClientRecordCount`. Unless `updateRecords=true`, server truncates to `clientRecordCount` before sort/paginate. `/api/news/count` is `IMemoryCache` + `PhysicalFileProvider.Watch(reports.json)` with 10-min sliding expiry.
6. **Per-item summaries:** `NewsDetail.summary` (`""`/null = not summarized yet) is cached in `reports.json` itself — no TTL, published text never changes. `GET /api/news/{id}/summary` returns cache or generates via `NewsSummary.summarizeDetail` (extract + NVIDIA) and persists with `JsonStore.updateSummary`; generation failures persist nothing so retry is safe. `/no_think` prefix gated by `DisableReasoning` (Nemotron-only, `NewsSummary.fs:8`); `SummarySettings` is just `Model|MaxChars|MaxRetries|DisableReasoning`. `updateSummary` serializes in-process writes with `writeLock`; `updateNewsCust` preserves cached summaries for re-fetched keys. Frontend: chevron + modal in both viewers; `report_viewer_legacy.html` must stay iOS 9-safe (XHR, no `fetch`/`Promise`/`classList`/flex — see passo 4).
7. **Tests:** `IntegrationTests.fs` boots a real server on `:8082`, writes `reports.json` (+`.bak` restore), sleeps 5s+1s — slow/flaky, fails if `:8082` busy. Unit suites (`*Tests.fs`) are safe; `ApiTests.fs` handler tests + `JsonStoreSummaryTests.fs` use temp dirs/files; live generation / live B3/CVM paths need network + `NVIDIA_API_KEY=nvapi-...`.
