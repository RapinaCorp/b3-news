# Current-day update fix — problem, fix, and intended semantics

## Problem

Two independent bugs prevented the `update` command from reliably refreshing reports from the current B3 day:

1. **Stale list cache**: `News.fetchAllNews` (`News.fs:66-115`) cached *every* successful fetch, including ranges whose end date is today. The first `update` of the day wrote a cache file into `news_cache/`; every later `update` that day served the morning snapshot, so reports published after the first run never reached `reports.json`.

2. **Wrong timezone default**: `Program.fs:38,43` defaulted missing date args to `DateTime.Today` (machine-local). On a host outside the B3 timezone (e.g., UTC before 21:00 UTC), "today" was actually yesterday from B3's perspective — the update targeted the wrong day entirely.

## Fix (already in the working tree)

`News.fs`:

- Added `todayB3()` — converts UTC to `America/Sao_Paulo` with Windows `E. South America Standard Time` fallback (`News.fs:43-50`).
- Added `isCacheable today endDate` — only returns `true` when `endDate.Date < today.Date` (`News.fs:54-55`). Intervals ending today or in the future are never cached.
- Moved `Directory.CreateDirectory(cacheDir)` inside the cacheable-write branch — no empty `news_cache/` dir on fresh non-cacheable runs (`News.fs:91-93`).
- When `not cacheable`, stale leftover cache files from older versions are deleted (`News.fs:103`).

`Program.fs`:

- Both `d1` and `d2` default to `News.todayB3().ToString("yyyy-MM-dd")` instead of `DateTime.Today` (`Program.fs:38,43`).

## Intended semantics

| Interval end | Cache? | Effect |
|---|---|---|
| Before yesterday | ✅ | Read/write cache as before. |
| Yesterday only | ✅ | Immutable; safe to serve from cache. |
| **Today** | ❌ | Always fresh fetch; delete any prior cache for this range. |
| **Future** | ❌ | Always fresh fetch; never served from cache. |

- A range that *starts* in the past but *ends* today (e.g. `update 2026-10-01` with one arg) is never cached — the default d2 ends at today. Users who want caching must pass an explicit closed range, e.g. `update 2026-10-01 2026-10-07`.
- Stale cache files from older versions are deleted per-range when a non-cacheable range is re-requested — self-correcting, not a full sweep.

## Tests added

Added unit tests to `B3News.Tests/NewsTests.fs` (see below) that verify the `isCacheable` boundary logic and a loose `todayB3` sanity check. No DI introduced — the decision logic is pure and testable without it, matching the repo convention from review-fixes step 01.

## Verification

- `dotnet build` — 0 warnings expected (same conditions as the review-fixes commits).
- `dotnet test B3News.Tests --filter "FullyQualifiedName!~IntegrationTests"` — all unit suites pass (no network/API-key dependencies outside IntegrationTests; confirmed offline-safe).