# Step 09 — `AGENTS.md`: net10.0 correction + plans convention

## Problem

`AGENTS.md` "Build / Run / Test" section states the fsproj targets
`net9.0` and that the README's ".NET 10" claim is wrong. Reality: the
fsproj targets `net10.0` and the owner confirms net10.0 — the AGENTS.md
gotcha is inverted and will mislead future agents.

## Fix

1. Replace the net9.0 bullet with: SDK is .NET 10, fsproj targets
   `net10.0` — trust the fsproj.
2. Add a line under "What to read first" (or a new bullet): implementation
   plans live in `docs/plans/<YYYY-MM-DD-name>/`, one numbered step file
   per change; write plans there before non-trivial work.

No behavior change.

## Verification

- `dotnet build` (docs don't compile; sanity only)
- Eyeball the rendered AGENTS.md diff.
