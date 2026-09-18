# Step 02 — `DetailsQuery.fs`: short-date crash, Array.Parallel, sortNews drops

## Problem a — `filterByDate` crashes on short/null `dateTime` (`DetailsQuery.fs:109`)

```fsharp
fun (x: NewsDetail) ->
    let date = x.dateTime.Substring(0, 10)
    not (String.IsNullOrWhiteSpace x.dateTime) && date >= startDate && date <= endDate
```

`Substring(0, 10)` runs **before** the guard: a null `dateTime` throws
`NullReferenceException`; a short one (`"2025-1-3"`, 8 chars) throws
`ArgumentOutOfRangeException`. It escapes through `filterAll` and surfaces
as a 500 in `/api/news`.

## Fix a

Guard first; malformed records are excluded from the date filter, never
fatal:

```fsharp
fun (x: NewsDetail) ->
    match x.dateTime with
    | null | "" -> false
    | s when s.Length >= 10 ->
        let d = s.Substring(0, 10)
        d >= startDate && d <= endDate
    | _ -> false // too short to hold yyyy-MM-dd: excluded, not fatal
```

## Problem b — `Array.Parallel` in `filterAll` (`DetailsQuery.fs:219-227`)

The dataset is a few thousand in-memory items; parallel partitioning is
pure overhead, and it wraps any exception (see problem a) in parallel-
schedule noise. Replace with a plain sequential filter:

```fsharp
let filtered =
    details
    |> List.filter (fun item -> filters |> List.forall (fun f -> f item))
```

## Problem c — `sortNews ByDate` silently drops records (`DetailsQuery.fs:178-188`)

`List.choose` discards items whose `dateTime` doesn't parse as
`"yyyy-MM-dd HH:mm:ss"` — sorted views can quietly under-report. Keep the
drop (changing what sorting *includes* is a product decision, not a bug
fix) but make it observable:

```fsharp
| ByDate ->
    let parsed, dropped =
        items
        |> List.partition (fun x ->
            match DateTime.TryParseExact(x.dateTime, "yyyy-MM-dd HH:mm:ss",
                                         CultureInfo.InvariantCulture, DateTimeStyles.None) with
            | true, _ -> true
            | _ -> false)

    if not dropped.IsEmpty then
        Log.warning (sprintf "sortNews: %d registros com data inválida fora da ordenação" dropped.Length)

    parsed
    |> List.sortBy (fun x -> DateTime.ParseExact(x.dateTime, "yyyy-MM-dd HH:mm:ss",
                                                CultureInfo.InvariantCulture, DateTimeStyles.None))
```

(`DetailsQuery.fs` has no `Log` dependency today — add `open Log`? No:
`Log` is a module compiled first, just call `Log.warning` directly; F#
modules don't need opens.)

## Tests (`DetailsQueryTests.fs`)

- Record with `dateTime = "2025-1-3"` (8 chars) + `filterByDate` — no
  exception, record excluded.
- Record with `dateTime = null` (record constructed with `null`) — no
  exception, excluded.
- Happy path unchanged: existing date-range tests must keep passing.
- `filterAll` result identical before/after Array.Parallel removal for a
  randomized list (existing tests cover ordering).

## Verification

- `dotnet build`
- `dotnet test B3News.Tests --filter "FullyQualifiedName~DetailsQueryTests"`
