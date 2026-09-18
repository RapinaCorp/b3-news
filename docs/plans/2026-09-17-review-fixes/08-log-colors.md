# Step 08 — `Log.fs`: error color to proper Red

## Problem

`Log.error` uses `ConsoleColor.Yellow` and `Log.warning` uses `DarkYellow` —
an error renders *less* alarming than a warning on most terminal palettes.

## Fix

```fsharp
let error message =
    printMessageWithColor ConsoleColor.Red message
```

`emergency` is also Red; the collision is acceptable for now (owner's
call). If distinguishing them later matters, options: `emergency` stays
Red / `error` goes Red on a highlighted background, or `alert`/`critical`
get reshuffled — out of scope here.

## Verification

- `dotnet build`
- Existing tests unaffected (Log has no tests; colors are cosmetic).
