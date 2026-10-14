# Step 06 — `Api.fs`: per-id in-flight summary dedupe

## Problem

`handleGetSummary` (`Api.fs:201-246`): two concurrent requests for the
same not-yet-summarized id each run the extract + LLM pipeline (double
cost and latency), then both persist. `updateSummary`'s `writeLock`
serializes the *write*, not the *generation*. The endpoint is
unauthenticated, so a burst on one id burns LLM quota N times.

## Fix — per-id gate + re-check inside the gate

Module state:

```fsharp
open System.Collections.Concurrent

let private summaryGates = ConcurrentDictionary<int, SemaphoreSlim>()
```

Handler flow when the freshly-read record has no cached summary:

```fsharp
let gate = summaryGates.GetOrAdd(id, fun _ -> new SemaphoreSlim(1, 1))
let! _ = gate.WaitAsync() |> Async.AwaitTask
try
    // Re-read: the winner of the race may have persisted while we waited.
    match JsonStore.tryRead Config.reportsJson with
    | Ok details ->
        match details |> List.tryFind (fun d -> d.id = id) with
        | Some d when not (String.IsNullOrWhiteSpace d.summary) ->
            return cached-summary-response
        | _ ->
            let! summary = generate ()   // NewsSummary.summarizeDetail
            persist + respond (existing logic: write failure still returns Ok)
    | Error err -> 500
finally
    gate.Release() |> ignore
```

Key property: **re-check the cache inside the gate** — the loser of the
race then serves the persisted summary instead of re-generating.

## Testability refactor

Pull the body into a helper that takes the generator as a function so the
test doesn't need the LLM:

```fsharp
// In Api.fs (or a small module) — generator injected
let getOrGenerateSummary (id: int) (read: unit -> Result<NewsDetail list, string>)
                         (generate: NewsDetail -> Async<Result<string, string>>)
                         (persist: int -> string -> Result<unit, string>) : Async<...>
```

The handler passes the real `JsonStore.tryRead` / `NewsSummary.summarizeDetail` /
`JsonStore.updateSummary`; the test passes fakes: an in-memory mutable
store and a generator that sleeps ~100ms, counts invocations, and returns
`Ok "s"`. Assert: two concurrent calls ⇒ generator invoked exactly once,
both calls return `"s"`.

`SemaphoreSlim` lifecycle: one per id, never removed. Bounded by the number
of distinct ids ever summarized (thousands at most — negligible memory).
Noted, accepted.

## Failure semantics (unchanged from current code)

- Generation failure ⇒ nothing persisted, error to the client (retry safe).
- Persist failure ⇒ log + still return the summary (current behavior,
  keep it — a failed cache write shouldn't waste a good generation).

## Tests (`ApiTests.fs`)

- Concurrent same-id generation runs the generator once (fake generator
  with counter + delay; `Async.Parallel` two calls).
- Different ids do not serialize each other (two ids generate
  concurrently — assert total wall time < 2× single generation, or simpler:
  assert the second gate isn't blocked via a timeout-based fake).
- Existing handler tests keep passing.

## Verification

- `dotnet build`
- `dotnet test B3News.Tests --filter "FullyQualifiedName~ApiTests"`
