# Step 07 — `Program.fs`: exit codes + real date validation

## Problem a — `usage()` always exits 0

Invalid command or invalid args exit with status 0, so scripts can't detect
misuse.

## Fix a

Parameterize:

```fsharp
let usage (exitCode: int) =
    ...
    exit exitCode

let main (args: string array) : int =
    if args.Length = 0 || args.[0] = "help" then
        usage 0
    match args.[0] with
    | "server" -> Api.startApiServer args[1..]
    | "update" -> updateReportsJson args[1..]
    | _ -> usage 1
```

## Problem b — regex-only date validation crashes later

`isDateFormat` accepts `2026-13-99` (`^\d{4}-\d{2}-\d{2}$`), which then
throws `FormatException` inside `News.parseDate` (`DateTime.ParseExact`) —
uncaught, process dies with a stack trace.

## Fix b

Validate with `DateTime.TryParseExact` and normalize; delete the regex:

```fsharp
let parseDateFormat (input: string) : DateTime option =
    match DateTime.TryParseExact(input, "yyyy-MM-dd",
                                 System.Globalization.CultureInfo.InvariantCulture,
                                 System.Globalization.DateTimeStyles.None) with
    | true, d -> Some d
    | _ -> None
```

In `updateReportsJson`: resolve both date args; any invalid ⇒ print a
clear error line (Log.error or printfn) with the expected format and
return 1. Then pass the *normalized* `d.ToString("yyyy-MM-dd")` on, so
`News.parseDate` can't throw on what survived validation.

Keep the default of "today" when a date arg is missing (current behavior).

`open System.Text.RegularExpressions` can be dropped if nothing else in
the file uses it.

## Behavior change (intentional)

`update <garbage>` and unknown commands now exit 1 instead of 0. `help`
and no-args still exit 0.

## Tests

CLI entry points aren't unit-tested today (Process-level harness is out
of scope). `parseDateFormat` is trivial enough that the fix is verified
by inspection + smoke test:

```
dotnet run -- update 2026-13-99   # expect: friendly error, exit 1
dotnet run -- bogus               # expect: usage, exit 1
dotnet run -- help                 # expect: usage, exit 0
```

If a cheap seam exists (top-level functions are in the implicit module for
`Program.fs`, tests can reference them — but `Program.fs` isn't compiled
into the test project; skip unit tests, keep the smoke check).

## Verification

- `dotnet build`
- Smoke the three CLI invocations above (check `$LASTEXITCODE`).
