# Step 04 — `Details.fs`: shared HttpClient + best-effort cache write

## Problem a — per-call HttpClient (`Details.fs:105`)

`fetchDetails` creates `use httpClient = new HttpClient()` per news item,
and `nwsMsgToNewsDetail` fans out via `Async.Parallel` — dozens of short-
lived clients per `update` run. Socket churn / port exhaustion under load.
`Download`, `OpenAIClient`, `ContentExtractor` already use module-level
clients.

## Fix a

Module-level client. Disposal note: the response returned from the cache
miss branch has its content read later by `getURLAsync` — with
`HttpCompletionOption.ResponseContentRead` (the default) the body is fully
buffered before `GetAsync` completes, so reading it after the async block
ends is safe; the module-level client is never disposed, which is the
correct lifecycle for a process-long singleton.

```fsharp
let private httpClient = new HttpClient()
```

Remove the `use` binding in the miss branch.

## Problem b — cache write race (`Details.fs:109`)

`transformAsync` runs `nwsMsgToNewsDetail` under `Async.Parallel`; if the
same (newsID, agency, dt) appears twice (B3 sometimes emits duplicates),
both coroutines take the `File.Exists = false` path and both open the same
`FileStream` (FileShare.None default) → `IOException` on the loser, which
is uncaught and fails the whole `update`.

## Fix b — cache write is best-effort

The response content is already in hand; a failed cache write must not
fail the update:

```fsharp
if response.IsSuccessStatusCode then
    let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
    try
        writeGzip cacheFile content
    with ex ->
        Log.warning (sprintf "cache write failed for %s: %s" cacheFile ex.Message)
```

## Deferred (noted, not done)

The fake-`HttpResponseMessage`-for-cache pattern in `fetchDetails` (a
hand-constructed 200 response) is awkward but behavior-preserving;
reshaping it to return the body string directly would touch
`nwsMsgToNewsDetail`'s status-code handling and `getURLAsync`'s input
type without fixing any observed bug.

## Tests

Existing suites cover `fetchDetails` paths. No new tests required for
this step (the race needs two parallel duplicate ids and network; the fix
is a try/with). Ensure `dotnet test` passes.

## Verification

- `dotnet build`
- `dotnet test B3News.Tests --filter "FullyQualifiedName!~IntegrationTests"`
