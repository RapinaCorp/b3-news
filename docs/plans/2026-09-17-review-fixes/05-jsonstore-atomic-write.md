# Step 05 — `JsonStore.fs`: atomic write + duplicate-key safety

## Problem a — non-atomic `reports.json` writes (`JsonStore.fs:16-21`)

`File.WriteAllText` truncates then writes; a crash/kill mid-write (or a
concurrent `server` + `update` process race, acknowledged in the code
comments) corrupts the store. Recovery today is manual.

## Fix a — temp file + atomic replace

```fsharp
let private tryWrite path details =
    try
        let json = JsonSerializer.Serialize(details, options)
        let tmp = path + ".tmp"
        File.WriteAllText(tmp, json)
        File.Move(tmp, path, true)   // same-volume atomic replace
        Ok()
    with e -> Error $"Error writing JSON: {e.Message}"
```

Same directory ⇒ same volume ⇒ rename is atomic on NTFS and POSIX. A crash
now leaves either the old file or the new one, never a truncated hybrid.
The `.tmp` sibling is covered by `reports.json` being gitignored
(verify `.gitignore` covers `reports.json.tmp` or `*.tmp` — if not, the
leftover temp after a crash would just sit there; harmless but untidy).

## Problem b — `Map.ofList` throws on duplicate keys (`JsonStore.fs:36`)

`existing |> List.map (fun d -> key d, d) |> Map.ofList` throws
`ArgumentException` if `reports.json` already contains two records with
the same `(ticker, dateTime, filename, url)` — possible from older merges
or hand edits. The whole `update` then fails on data that should merge.

## Fix b — last-wins fold

```fsharp
let existingByKey =
    existing |> List.fold (fun (m: Map<_, NewsDetail>) d -> m.Add(key d, d)) Map.empty
```

`Map.Add` overwrites silently (last wins). Also apply the same pattern to
any other `Map.ofList` over data-derived keys in this module (grep).

## Tests (`JsonStoreSummaryTests.fs` pattern: temp dirs)

- Duplicate-key `reports.json` (two identical-key records, different
  summaries) → the merge/read path does not throw; last one wins.
- `tryWrite` still round-trips (existing tests).
- A `path + ".tmp"` leftover from a previous crash is overwritten cleanly
  (`File.Move(..., true)`).

## Verification

- `dotnet build`
- `dotnet test B3News.Tests --filter "FullyQualifiedName~JsonStore"`
