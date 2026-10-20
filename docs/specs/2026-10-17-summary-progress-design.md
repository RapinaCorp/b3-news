# Summary Progress Status — Design (`GET /api/news/{id}/summary`)

## 1. Goal & Scope

Today `GET /api/news/{id}/summary` blocks until the whole pipeline
(`ContentExtractor.extractText` + `OpenAIClient.summarize`) finishes, so the
viewers can only show a static "Gerando resumo…". This change makes the
endpoint return the current step immediately and lets both viewers poll until
the final summary (or an error) arrives:

1. Client GETs the endpoint.
2. Response is either `{summary}` (already cached) or `{status, message}`
   (generation running, `fetching` → `summarizing`).
3. Client polls while it sees `status`; `summary` or an error ends the loop.

In scope: `Api.fs`, `NewsSummary.fs`, `B3News.Tests/ApiTests.fs`,
`report_viewer.html`, `report_viewer_legacy.html`, plus docs (`AGENTS.md`,
`CONTEXT.md`). Out of scope: SSE/websockets, persistence of job state, global
concurrency caps, JS test harness.

## 2. API Contract

| Situation | Status | Body |
|---|---|---|
| Invalid id | 400 | `{error}` (unchanged) |
| Unknown id | 404 | `{error}` (unchanged) |
| Store read failure (handler) | 500 | `{error}` (unchanged) |
| Summary cached in `reports.json` | 200 | `{summary}` (unchanged) |
| No LLM key and no job running | 500 | `{error}`, **no job started** (preserves current test) |
| Uncached, no job → job started by this request | 200 | `{status:"fetching", message:"Buscando o conteúdo do comunicado…"}` |
| Job running — extraction step | 200 | `{status:"fetching", message:"Buscando o conteúdo do comunicado…"}` |
| Job running — LLM step | 200 | `{status:"summarizing", message:"Gerando o resumo com IA…"}` |
| Job finished, persist OK | 200 | `{summary}` (store cache hit; registry entry already removed) |
| Job finished, persist FAILED | 200 | `{summary}` (served **once** from memory, entry removed on read) |
| Job failed (generation) | 502 | `{error}` — reply computed from the failed state, then entry removed |
| Job failed (store re-read) | 500 | `{error}` — same removal semantics |

The server owns both the `status` token (machine) and `message` (PT text);
viewers display `message` and only branch on `status`/`summary` presence.

## 3. State Machine

```
handler                                          registry (in-memory)
   │                                                    │
   │  probe ──► Running step     → {status, message}    │
   │         ──► Succeeded s     → {summary:s} + remove │
   │         ──► Failed e        → {error}     + remove │
   │         ──► miss                                    │
   │              tryRead ──► 404 / cached {summary}    │
   │              key check ──► 500 (no job)            │
   │              TryAdd(id, Running Fetching) ─────────►│
   │                  └─ won? ──► 200 {status:"fetching"}│
   │                  └─ lost ──► re-dispatch (≤3), else 500
   │                                           pipeline │
   │                                    re-read store   │
   │                                     ├─ cached ──► remove entry
   │                                     ├─ error  ──► Failed(StoreError)
   │                                     └─ generate(onStep)
   │                                          onStep ──► Running step
   │                                          Ok s ──► persist
   │                                             ├─ Ok    ──► remove entry
   │                                             └─ Error ──► Succeeded s
   │                                          Error e ──► Failed(GenerationError e)
   │                                          exception ► Failed(GenerationError msg)
```

### Concurrency invariants (must hold in implementation)

1. **`TryAdd` is the only job gate.** Exactly one pipeline per id; the
   semaphore dictionary (`summaryGates`) and `getOrGenerateSummary` are
   deleted.
2. **Single writer per entry.** Only the owning pipeline writes `Running`
   transitions and the terminal transition. Readers never remove `Running`.
3. **Terminal replies are idempotent.** On a terminal state the handler
   computes the reply from the state it read and then does a best-effort
   `TryRemove`. Two concurrent polls both receive the terminal answer; the
   loser does **not** restart generation.
4. **Persist completes before the terminal transition.** `Ok` → remove entry;
   `Error` → leave `Succeeded summary`. This prevents a poll from re-dispatching
   while the owning pipeline is still writing.
5. **The pipeline re-reads `reports.json` once after claiming the job**, before
   generating (preserves the old "re-read inside the gate" semantics: a request
   that waited behind a completed generation must not regenerate).
6. **Total `try/with` around the pipeline**, including `onStep` invocations —
   any surprise becomes `Failed(GenerationError …)` via `Log.error`, never a
   stuck `Running`.
7. **Registry is probed before `tryRead`** so 2 s polls do not re-read and
   deserialize `reports.json` while a job runs, and a transiently unreadable
   store does not break progress polling.
8. **Bounded re-dispatch on a lost `TryAdd` race**: re-probe the registry up to
   3 times (normally the winner's state is served on the first retry); after
   that, 500 with a clear error.

## 4. Backend Changes

### `NewsSummary.fs`

```fsharp
/// Etapa corrente do pipeline de resumo, reportada a quem inicia o job.
type SummaryStep =
    | Fetching
    | Summarizing

let summarizeDetail
    (endpoint: string) (apiKey: string) (model: string)
    (maxRetries: int) (maxChars: int) (disableReasoning: bool)
    (onStep: SummaryStep -> unit)      // NEW, before `detail`
    (detail: NewsDetail)
    : Async<Result<string, string>> =
    async {
        onStep Fetching
        let! extracted = ContentExtractor.extractText detail.url maxChars
        … // unchanged
        onStep Summarizing
        let! summary = OpenAIClient.summarize …
        … // unchanged
    }
```

`SummaryStep` lives in `NewsSummary` (loaded before `Api`); `Api.fs` uses it
qualified (`NewsSummary.Fetching`). No `B3News.fsproj` change needed.

### `Api.fs` — types and orchestration

```fsharp
/// Estado em memória de um job de resumo (não persistido).
type SummaryState =
    | Running of NewsSummary.SummaryStep
    | Succeeded of summary: string   // persist falhou: servido uma vez e removido
    | Failed of SummaryErr           // próxima leitura responde o erro e remove

/// Decisão de resposta para uma requisição de resumo.
type SummaryDecision =
    | ServeSummary of string
    | ServeStep of NewsSummary.SummaryStep
    | FailSummary of SummaryErr
    | MissingKey of string
    | SummaryNotFound
    | StartRequested of Details.NewsDetail

/// Payload da resposta de progresso.
let summaryStepPayload (step: NewsSummary.SummaryStep) : obj =
    match step with
    | NewsSummary.Fetching ->
        box {| status = "fetching"; message = "Buscando o conteúdo do comunicado…" |}
    | NewsSummary.Summarizing ->
        box {| status = "summarizing"; message = "Gerando o resumo com IA…" |}

let decideSummaryRequest
    (jobs: ConcurrentDictionary<int, SummaryState>)
    (read: unit -> Result<Details.NewsDetail list, string>)
    (apiKey: unit -> Result<string, string>)
    (id: int) : SummaryDecision

let runSummaryJob
    (jobs: ConcurrentDictionary<int, SummaryState>)
    (read: unit -> Result<Details.NewsDetail list, string>)
    (generate: Details.NewsDetail -> (NewsSummary.SummaryStep -> unit) -> Async<Result<string, string>>)
    (persist: int -> string -> Result<unit, string>)
    (detail: Details.NewsDetail) : Async<unit>

let startSummaryJob
    (jobs: ConcurrentDictionary<int, SummaryState>)
    (read: …) (generate: …) (persist: …)
    (detail: Details.NewsDetail) : bool
```

`decideSummaryRequest` is synchronous and does no job starting; it maps
registry + store + key to a `SummaryDecision` (probe order as in §3).

`runSummaryJob` implements invariants 4–6 (single writer, persist-then-state,
re-read, total `try/with`, `Log.warning` on persist failure).

`startSummaryJob` does `jobs.TryAdd(detail.id, Running Fetching)`; on success
logs `Log.info`, launches `runSummaryJob` fire-and-forget (`task { … } |> ignore`
with the pipeline's own total catch), returns `true`; on `TryAdd` false returns
`false`.

`persist`'s signature becomes `int -> string -> Result<unit, string>` (it is
`JsonStore.updateSummary Config.reportsJson`), so the pipeline owns the
warning log. The handler's previous inline persist wrapper is deleted.

Handler (injected deps, mirroring `handleNumberOfReports memoryCache`):

```fsharp
let handleGetSummaryWith
    (jobs: ConcurrentDictionary<int, SummaryState>)
    (read: unit -> Result<Details.NewsDetail list, string>)
    (apiKey: unit -> Result<string, string>)
    (startJob: Details.NewsDetail -> bool)
    (ctx: HttpContext) : Task

let handleGetSummary = handleGetSummaryWith summaryJobs realRead Config.llmApiKey realStartJob
```

Real composition uses module-level `let private summaryJobs =
ConcurrentDictionary<int, SummaryState>()` and a `generate` that reads
`Config.llmApiKey()` / `Config.readSummarySettings()` per job (inside the
pipeline's try, so a vanishing key surfaces as `Failed`).

## 5. Frontend Changes

### `report_viewer.html`

- Constants: `SUMMARY_POLL_INTERVAL_MS: 2000`,
  `SUMMARY_POLL_MAX_ATTEMPTS: 900` (30 min; worst-case pipeline ≈ 13 min).
- `state.summaryRequestToken: 0` (monotonic).
- `openSummaryPopup(id, title, summaryText)`:
  - cache branch unchanged (`summaryText || summaryCache[id]` → render, done);
  - otherwise `await pollSummary(id, title)`.
- `pollSummary` loop, each iteration guarded by `isCurrentSummaryRequest(req)`
  (token match + `body.dataset.summaryId === String(id)` + modal visible):
  - 200 `{summary}` → `summaryCache[id] = summary`, `renderSummaryBullets`, done;
  - 200 `{status,message}` → render `data.message || data.status` in the
    existing gray placeholder style, `await delay(2000)`, next attempt;
  - non-OK → existing `showSummaryError` (error + "Tentar de novo"), stop;
  - network exception → "Erro de conexão ao gerar o resumo.", stop;
  - after 900 attempts → "Tempo esgotado gerando o resumo." + retry.
- Closing the modal bumps the token; the in-flight loop notices and stops
  without touching the body. Retry re-invokes `openSummaryPopup`, which starts
  a fresh token.
- The ▾ button color update logic in `renderTable` stays as is: it only turns
  green when `summaryCache[id] || report.summary` exists.

### `report_viewer_legacy.html` (ES5 / iOS 9 safe)

Same algorithm with `XMLHttpRequest` + `setTimeout` + `var`; no
`fetch`/`Promise`/`classList`/arrow functions. Differences:

- `summaryPollToken` module variable; `openSummaryPopup` sets
  `body.setAttribute('data-summary-id', String(id))`.
- `hideSummaryModal()` bumps the token and calls `xhr.abort()` on the tracked
  in-flight request.
- `isCurrentSummaryPoll(poll)` checks token + data attribute + modal display.
- Timeout message and retry identical to the modern viewer.

## 6. Memory Analysis

- Running jobs: one `SummaryState` DU + closure per in-flight id.
- Terminal entries (`Succeeded`/`Failed`): removed on the next read
  (best-effort, computed reply). A terminal entry whose client vanished
  lingers — bounded by the rarity of persist failures/failed generations, and
  strictly smaller than today's `summaryGates`, which accumulates one
  `SemaphoreSlim` per distinct id **forever** (`Api.fs` comment).
- No TTL/eviction timer: rejected as machinery with no practical win over the
  current leak profile.

## 7. Testing Strategy

`ApiTests.fs` (events, not sleeps; fresh `ConcurrentDictionary` per test):

1. First decision on uncached item with key → `StartRequested`; no key →
   `MissingKey`.
2. `decideSummaryRequest` with `Running Summarizing` → `ServeStep`, and `read`
   is never invoked.
3. Running job blocks on a `ManualResetEventSlim` inside the injected
   `generate`; test asserts `RunSummaryJob` reported `Running Fetching` then
   `Running Summarizing` via the `onStep` callback, then releases it.
4. `startSummaryJob` twice for the same id → second returns `false`, generator
   called exactly once (replaces the two-concurrent-requests test).
5. Different ids both start (analog of the existing cross-id test).
6. Re-read finds cached summary → generate not called, entry removed.
7. `generate` Error → `Failed(GenerationError …)`; generator throws →
   `Failed(GenerationError …)` (no stuck `Running`).
8. `read` Error → `Failed(StoreError …)`.
9. Persist Error → `Succeeded summary`; `decideSummaryRequest` serves it and
   removes the entry; next decision → `StartRequested` (retry restarts).
10. Terminal reply idempotence: with a pre-seeded `Failed`, two
    `decideSummaryRequest` calls both return `FailSummary` (entry removed after
    the first).
11. `summaryStepPayload` serializes to the exact `status`/`message` pair.
12. Existing tests stay green: cached summary, 400 invalid id, 404 unknown id,
    500 no key (no job started).

Manual smoke (network + `NVIDIA_API_KEY` required): `dotnet run -- server`,
click ▾ on an uncached item in `/` and `/legacy`, observe step messages then
the summary; close/reopen mid-generation; retry after forcing a failure.

## 8. Review Findings & Resolutions

Findings from the architecture review performed before implementation:

| # | Finding | Resolution |
|---|---|---|
| B1 | `TryAdd` loser behavior unspecified | Invariant 8: bounded re-dispatch; tests 4/10 |
| B2 | `Failed of string` cannot map 502 vs 500 | `Failed of SummaryErr` (StoreError→500, GenerationError→502); store error reachable via pipeline re-read |
| B3 | Re-read-inside-gate guarantee dropped | Invariant 5: pipeline re-reads before generating; test 6 |
| I1 | Registry probe before `tryRead` | Invariant 7 (polls avoid file I/O; resilient to unreadable store mid-job) |
| I2 | Serve-once races / never-read entries | Keep serve-once (user decision) + invariant 3 (idempotent replies fix the restart race). TTL rejected: today's `summaryGates` leaks more; documented |
| I3 | Persist must precede terminal transition | Invariant 4 |
| I4 | Stuck `Running` on unexpected throw | Invariant 6 + tests 7; stale-eviction rejected (a stale task could clobber a newer job; realistic hangs are bounded by HTTP timeouts) |
| I5 | Design untestable without a seam | `handleGetSummaryWith` injected deps (precedent: `handleNumberOfReports memoryCache`); tests 1–12 |
| M1 | Lock-protected holder unneeded | Plain DU in `ConcurrentDictionary`; single writer |
| M2 | 3 min budget shorter than plausible run | 900 × 2 s = 30 min |
| M3 | No global in-flight cap | Pre-existing; documented, out of scope |
| M4 | Background persist widens cross-process `update` window | Documented in `AGENTS.md`; not fixed here |
| M5 | Late continuation can render into a newer popup | Monotonic request token guard in both viewers |
| N1 | New module would need fsproj slot | Types stay in `Api.fs` |
| N2 | No F# `option` in serialized payloads | Anonymous records boxed to `obj` |
| N3 | Docs stale after behavior change | `AGENTS.md` gotcha 6 + `CONTEXT.md` `SummaryJob` entry |
| N4 | Move persist warning; lifecycle logs | Pipeline owns warning; `Log.info` start/finish; `Log.error` on exception |
| N5 | Machine tokens in EN, presentational in PT | `status` values in EN, `message` in PT, ellipsis `…` |

## 9. Known Limitations (accepted)

- Job state is in-memory: a server restart mid-generation loses the job and the
  next poll starts a new one.
- A persist failure leaves a `Succeeded` entry until read; if the client
  disappears it lingers (see §6).
- Cross-process writes (`update` while a job persists) keep the existing
  last-writer window; `writeLock` only serializes in-process writes.
- Cross-id parallelism is unbounded (pre-existing). Do not ship a
  "summarize all" UI on this design without adding a cap.

## 10. Post-implementation Review Adjustments

From the code review after implementation:

- Jobs start with `Async.Start` (threadpool), not `task { do! … }`: F# starts
  async/task bodies synchronously until the first real yield, which ran the
  pipeline inline on the request thread (and deadlocked tests with blocking
  fakes).
- State transitions are written before terminal logs, and job-lifecycle logs
  are `try/with`-guarded: a throwing `Log.*` could otherwise leave an entry
  `Running` forever or clobber a newer job's state.
- Viewers: 200-with-invalid-JSON now shows an error + retry instead of
  stranding the modal; legacy network failure (`xhr.status === 0`) reuses the
  modern connection-error message; the retry path updates row state through
  the new `markSummaryLoaded` helper.
- Tests: parallel-ids test uses `Interlocked.Increment`; added coverage for
  re-dispatch exhaustion (500) and handler-level serve-once; `readSummaryResponse`
  now leaves the response body stream open.
- `README.md` endpoint/behavior sections updated to the new contract.
