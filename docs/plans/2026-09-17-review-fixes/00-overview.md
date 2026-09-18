# Review fixes — bug fixes and code smells from the 2026-09-17 code review

## Context

A full code review (all `.fs` sources, both viewers, fsproj, tests) found one
confirmed logic bug, several crash surfaces, and a set of smells. The build
passes with 0 warnings. This plan fixes them one step per file. The Fnet
base64-quoted fetch semantics (`Download.fetchBytes` Fnet branch) are
**intentionally left untouched** — the owner is verifying against the live
site whether that response format is still real; a decision will come later.

Steps are independent of each other (one file per step) except that tests
for a step are written in the same step. Execute in numeric order; run
`dotnet build` after each step and the test suite at the end.

Out of scope: Fnet fetch semantics, `b3news.service` (stale deploy file),
the HTML viewers (verified clean: all data rendered via `textContent`),
Newtonsoft.Json pinning (deliberate, documented in fsproj).

## Steps

| # | File | Fix |
|---|------|-----|
| 01 | `News.fs` | Cache-hit fallthrough bug (`return Ok data |> ignore`) + module-level HttpClient |
| 02 | `DetailsQuery.fs` | `Substring(0,10)` crash guard; drop `Array.Parallel`; log `sortNews` drops |
| 03 | `Download.fs` | `removeQuotes` off-by-one on `""`; `detectKind` bounded 64KB sniff |
| 04 | `Details.fs` | Module-level HttpClient; best-effort cache write (parallel race) |
| 05 | `JsonStore.fs` | Atomic write (temp + `File.Move`); duplicate-key-safe map |
| 06 | `Api.fs` | Per-id in-flight summary dedupe (double LLM cost + quota abuse) |
| 07 | `Program.fs` | Exit code 1 on invalid command/args; `TryParseExact` date validation |
| 08 | `Log.fs` | `error` color to proper Red |
| 09 | `AGENTS.md` | Fix stale net9.0 statement; document the plans convention |

## Embedded decisions (flag to the owner if unwanted)

- `detectKind` sniffs the first 64 KB instead of the whole body (memory
  vs exact legacy semantics; `%PDF` magic is at offset 0 in practice).
- `sortNews` keeps dropping unparseable-date records but logs the count
  (visibility without changing pagination semantics).
- Log colors: `error` becomes Red, same as `emergency` — acceptable collision
  for now.
- AGENTS.md is edited (it's the agent guide, factually wrong about net9.0).

## Verification

1. `dotnet build` after each step — 0 warnings expected.
2. `dotnet test` — full suite needs port 8082 free for `IntegrationTests`;
   use `dotnet test --filter "FullyQualifiedName!~IntegrationTests"` if busy,
   and run the integration suite once on a clean port.
3. Smoke: `dotnet run -- update <today>` and one
   `GET /api/news/{id}/summary` against a seeded `reports.json`.
