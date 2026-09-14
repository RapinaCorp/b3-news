# B3News — Agent Guide

F# / .NET console + minimal API. Single app project + xunit test project.

## Build / Run / Test
- SDK: README says .NET 10 SDK, but `B3News.fsproj:5` targets `net9.0` — trust fsproj; `dotnet build` works with either.
- `dotnet build`
- `dotnet run -- <command>` — note `--` to forward args:
  - `update-json <data1> [<data2>]` (dates `YYYY-MM-DD`; missing arg defaults to today in `Program.fs:23`)
  - `summarize` (requires `NVIDIA_API_KEY=nvapi-...`)
  - `serve-api` (Kestrel `http://localhost:8082` from `b3news.settings.json`)
- Tests: `dotnet test`
- Single test: `dotnet test B3News.Tests --filter "FullyQualifiedName~<TestName>"`
- Do NOT trust `b3news.service:11` (`B3News daemon`) — no `daemon` command exists in `Program.fs:92`; stale deploy file.

## What to read first
- `CONTEXT.md` — normative `Source | Kind | FetchErr | SaveOutcome` glossary. Don't duplicate; follow it.
- `Program.fs` (CLI entry), `Api.fs:217` (routes), `Config.fs` (settings), `Download.fs:81,185` (classify/fetch), `JsonStore.fs:23` (merge/dedupe).

## Gotchas agents will miss
1. **F# file order is load order.** `B3News.fsproj:13-27` lists compile order (`Log → … → Api → Program`). Insert new `.fs` files in dependency position, not alphabetically.
2. **All paths are CWD-relative.** `Config.fs:62` + `reportsJson="reports.json"` resolve via `Directory.GetCurrentDirectory()`. Run from repo root. `b3news.settings.json` and `reports.json` are gitignored — missing file = empty tickers / empty list, not an error.
3. **`Download.classify` is the only URL router** (`Fnet | Enet | EnetNbc | Unknown`). Never re-regex URLs; pattern-match `Source`. Enet flow needs viewer-GET warmup + POST to `/ENETWEB/.../ExibirPDF` with 3x SSL-only retry; `EnetNbc`/`Unknown` → `BadUrl`.
4. **`FetchErr` bodies truncated to 256 chars** at boundary (`Download.fs:99`); use `describeFetchErr` / `httpStatusForFetchErr` for logs and `/pdf/{id}.pdf` responses. `detectKind` defaults to `Pdf` unless `<HTML>` found.
5. **Snapshot pagination:** `Api.parseQuery` + `DetailsQuery.withClientRecordCount`. Unless `updateRecords=true`, server truncates to `clientRecordCount` before sort/paginate. `/api/news/count` is `IMemoryCache` + `PhysicalFileProvider.Watch(reports.json)` with 10-min sliding expiry.
6. **Summarize quirks:** `/no_think` prefix gated by `DisableReasoning` (Nemotron-only, `SummaryPipeline.fs:22`); consolidated summary requires ≥5 docs else placeholder; `MaxParallelism` clamps via `max 1`, outer groups run with parallelism 1.
7. **Tests:** `IntegrationTests.fs` boots a real server on `:8082`, writes `reports.json` (+`.bak` restore), sleeps 5s+1s — slow/flaky, fails if `:8082` busy. Unit suites (`*Tests.fs`) are safe; `summarize`/live B3/CVM paths need network + `NVIDIA_API_KEY`.
