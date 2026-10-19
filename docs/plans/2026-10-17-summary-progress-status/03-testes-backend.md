# Passo 3 — Testes backend (`ApiTests.fs`)

## Imports e helpers

Adicionar aos imports do topo: `open System.Collections.Concurrent` e
`open System.Threading`.

Dividir o helper de execução para reaproveitar a leitura da resposta em
handlers injetados:

```fsharp
let private readSummaryResponse (ctx: DefaultHttpContext) =
    ctx.Response.Body.Seek(0L, SeekOrigin.Begin) |> ignore
    use sr = new StreamReader(ctx.Response.Body)
    ctx.Response.StatusCode, sr.ReadToEnd()

let private runSummaryHandler (ctx: DefaultHttpContext) =
    (handleGetSummary ctx).GetAwaiter().GetResult()
    readSummaryResponse ctx
```

Adicionar perto dos testes novos:

```fsharp
let private freshJobs () = ConcurrentDictionary<int, SummaryState>()

let private runJob jobs read generate persist detail =
    runSummaryJob jobs read generate persist detail |> Async.RunSynchronously
```

## Remover

Os cinco testes do `getOrGenerateSummary` (linhas ~211-330):

- `getOrGenerateSummary runs the generator once for concurrent same-id requests`
- `getOrGenerateSummary lets different ids generate concurrently`
- `getOrGenerateSummary skips generation when re-read finds a summary`
- `getOrGenerateSummary maps generation failure to GenerationError`
- `getOrGenerateSummary maps store read failure to StoreError`

As funções não existem mais (passo 1); a substituição é a suíte abaixo.

## Adicionar

```fsharp
[<Fact>]
let ``decideSummaryRequest pede início quando não há cache nem job`` () =
    let jobs = freshJobs ()
    let d = summaryDetail 301 ""

    match decideSummaryRequest jobs (fun () -> Ok [ d ]) (fun () -> Ok "chave") d.id with
    | StartRequested started -> Assert.Equal(d.id, started.id)
    | other -> failwithf "esperado StartRequested, veio %A" other

    Assert.False(jobs.ContainsKey d.id)

[<Fact>]
let ``decideSummaryRequest serve status de job rodando sem ler o store`` () =
    let jobs = freshJobs ()
    jobs.[302] <- Running NewsSummary.Summarizing
    let readCalled = ref false

    let decision =
        decideSummaryRequest
            jobs
            (fun () ->
                readCalled := true
                Ok [])
            (fun () -> Ok "chave")
            302

    match decision with
    | ServeStep NewsSummary.Summarizing -> ()
    | other -> failwithf "esperado ServeStep Summarizing, veio %A" other

    Assert.False(!readCalled)

[<Fact>]
let ``decideSummaryRequest sem chave e sem job não inicia nada`` () =
    let jobs = freshJobs ()
    let d = summaryDetail 303 ""

    match decideSummaryRequest jobs (fun () -> Ok [ d ]) (fun () -> Error "sem chave") d.id with
    | MissingKey err -> Assert.Equal("sem chave", err)
    | other -> failwithf "esperado MissingKey, veio %A" other

    Assert.False(jobs.ContainsKey d.id)

[<Fact>]
let ``runSummaryJob reporta etapas e remove a entrada após persistir`` () =
    let jobs = freshJobs ()
    let d = summaryDetail 304 ""
    jobs.[d.id] <- Running NewsSummary.Fetching
    let observed = ResizeArray<SummaryState>()
    let persisted = ref ""

    let generate (_: NewsDetail) (onStep: NewsSummary.SummaryStep -> unit) =
        async {
            onStep NewsSummary.Fetching
            observed.Add jobs.[d.id]
            onStep NewsSummary.Summarizing
            observed.Add jobs.[d.id]
            return Ok "resumo novo"
        }

    let persist id summary =
        Assert.Equal(d.id, id)
        persisted.Value <- summary
        Ok()

    runJob jobs (fun () -> Ok [ d ]) generate persist d

    Assert.Equal<SummaryState list>(
        [ Running NewsSummary.Fetching; Running NewsSummary.Summarizing ],
        List.ofSeq observed
    )

    Assert.Equal("resumo novo", !persisted)
    Assert.False(jobs.ContainsKey d.id)

[<Fact>]
let ``runSummaryJob não gera quando o resumo já foi persistido`` () =
    let jobs = freshJobs ()
    let d = summaryDetail 305 ""
    jobs.[d.id] <- Running NewsSummary.Fetching
    let generateCalled = ref false

    let generate (_: NewsDetail) (_: NewsSummary.SummaryStep -> unit) =
        async {
            generateCalled := true
            return Ok "não deveria"
        }

    let cached = { d with summary = "já persistido" }
    runJob jobs (fun () -> Ok [ cached ]) generate (fun _ _ -> Ok()) d

    Assert.False(!generateCalled)
    Assert.False(jobs.ContainsKey d.id)

[<Fact>]
let ``runSummaryJob mapeia erro de geração para Failed GenerationError`` () =
    let jobs = freshJobs ()
    let d = summaryDetail 306 ""
    jobs.[d.id] <- Running NewsSummary.Fetching

    let generate (_: NewsDetail) (_: NewsSummary.SummaryStep -> unit) =
        async { return Error "LLM indisponível" }

    runJob jobs (fun () -> Ok [ d ]) generate (fun _ _ -> Ok()) d

    match jobs.TryGetValue d.id with
    | true, Failed(GenerationError err) -> Assert.Equal("LLM indisponível", err)
    | state -> failwithf "esperado Failed(GenerationError), veio %A" state

[<Fact>]
let ``runSummaryJob mapeia falha de re-leitura para Failed StoreError`` () =
    let jobs = freshJobs ()
    let d = summaryDetail 307 ""
    jobs.[d.id] <- Running NewsSummary.Fetching

    runJob
        jobs
        (fun () -> Error "json corrompido")
        (fun _ _ -> async { return Ok "x" })
        (fun _ _ -> Ok())
        d

    match jobs.TryGetValue d.id with
    | true, Failed(StoreError err) -> Assert.Equal("json corrompido", err)
    | state -> failwithf "esperado Failed(StoreError), veio %A" state

[<Fact>]
let ``runSummaryJob com generate que lança vira Failed e não deixa Running`` () =
    let jobs = freshJobs ()
    let d = summaryDetail 308 ""
    jobs.[d.id] <- Running NewsSummary.Fetching

    let generate (_: NewsDetail) (_: NewsSummary.SummaryStep -> unit) =
        async { return failwith "boom" }

    runJob jobs (fun () -> Ok [ d ]) generate (fun _ _ -> Ok()) d

    match jobs.TryGetValue d.id with
    | true, Failed(GenerationError _) -> ()
    | state -> failwithf "esperado Failed(GenerationError), veio %A" state

[<Fact>]
let ``persist falha expõe o resumo uma vez e depois reinicia`` () =
    let jobs = freshJobs ()
    let d = summaryDetail 309 ""
    jobs.[d.id] <- Running NewsSummary.Fetching

    let generate (_: NewsDetail) (_: NewsSummary.SummaryStep -> unit) =
        async { return Ok "resumo gerado" }

    runJob jobs (fun () -> Ok [ d ]) generate (fun _ _ -> Error "disco cheio") d

    match jobs.TryGetValue d.id with
    | true, Succeeded "resumo gerado" -> ()
    | state -> failwithf "esperado Succeeded, veio %A" state

    match decideSummaryRequest jobs (fun () -> Ok [ d ]) (fun () -> Ok "chave") d.id with
    | ServeSummary s -> Assert.Equal("resumo gerado", s)
    | other -> failwithf "esperado ServeSummary, veio %A" other

    match decideSummaryRequest jobs (fun () -> Ok [ d ]) (fun () -> Ok "chave") d.id with
    | StartRequested _ -> ()
    | other -> failwithf "esperado StartRequested, veio %A" other

[<Fact>]
let ``leitura terminal repetida devolve o mesmo erro e remove a entrada`` () =
    let jobs = freshJobs ()
    jobs.[310] <- Failed(GenerationError "falhou")

    match decideSummaryRequest jobs (fun () -> Ok []) (fun () -> Ok "chave") 310 with
    | FailSummary(GenerationError "falhou") -> ()
    | other -> failwithf "esperado FailSummary, veio %A" other

    Assert.False(jobs.ContainsKey 310)

    let d = summaryDetail 310 ""
    match decideSummaryRequest jobs (fun () -> Ok [ d ]) (fun () -> Ok "chave") 310 with
    | StartRequested _ -> ()
    | other -> failwithf "esperado StartRequested, veio %A" other

[<Fact>]
let ``startSummaryJob inicia um único job por id`` () =
    let jobs = freshJobs ()
    let d = summaryDetail 311 ""
    let gate = new ManualResetEventSlim(false)
    let doneEvent = new ManualResetEventSlim(false)
    let calls = ref 0

    let generate (_: NewsDetail) (_: NewsSummary.SummaryStep -> unit) =
        async {
            incr calls
            gate.Wait()
            return Ok "resumo"
        }

    let persist (_: int) (_: string) =
        doneEvent.Set()
        Ok()

    Assert.True(startSummaryJob jobs (fun () -> Ok [ d ]) generate persist d)
    Assert.False(startSummaryJob jobs (fun () -> Ok [ d ]) generate persist d)

    gate.Set()
    Assert.True(doneEvent.Wait(TimeSpan.FromSeconds 5.0))
    Assert.Equal(1, !calls)

[<Fact>]
let ``startSummaryJob de ids diferentes roda em paralelo`` () =
    let jobs = freshJobs ()
    let d1 = summaryDetail 312 ""
    let d2 = summaryDetail 313 ""
    let gate = new ManualResetEventSlim(false)
    let bothStarted = new CountdownEvent 2
    let done = new CountdownEvent 2
    let calls = ref 0

    let generate (d: NewsDetail) (_: NewsSummary.SummaryStep -> unit) =
        async {
            incr calls
            bothStarted.Signal() |> ignore
            gate.Wait()
            done.Signal() |> ignore
            return Ok(sprintf "resumo %d" d.id)
        }

    let persist (_: int) (_: string) = Ok()

    Assert.True(startSummaryJob jobs (fun () -> Ok [ d1 ]) generate persist d1)
    Assert.True(startSummaryJob jobs (fun () -> Ok [ d2 ]) generate persist d2)

    // Ambos precisam estar gerando ao mesmo tempo para destravar o countdown.
    Assert.True(bothStarted.Wait(TimeSpan.FromSeconds 5.0))
    gate.Set()
    Assert.True(done.Wait(TimeSpan.FromSeconds 5.0))
    Assert.Equal(2, !calls)

[<Fact>]
let ``handleGetSummaryWith responde fetching quando inicia o job`` () =
    let jobs = freshJobs ()
    let d = summaryDetail 314 ""
    let ctx = summaryCtx (string d.id)
    let started = ref 0

    (handleGetSummaryWith
        jobs
        (fun () -> Ok [ d ])
        (fun () -> Ok "chave")
        (fun detail ->
            Assert.Equal(d.id, detail.id)
            incr started
            true)
        ctx)
        .GetAwaiter()
        .GetResult()

    let status, body = readSummaryResponse ctx
    Assert.Equal(200, status)
    use doc = JsonDocument.Parse(body)
    Assert.Equal("fetching", doc.RootElement.GetProperty("status").GetString())
    Assert.Equal("Buscando o conteúdo do comunicado…", doc.RootElement.GetProperty("message").GetString())
    Assert.Equal(1, !started)

[<Fact>]
let ``handleGetSummaryWith re-consulta quando perde a corrida do TryAdd`` () =
    let jobs = freshJobs ()
    let d = summaryDetail 315 ""
    let ctx = summaryCtx (string d.id)
    let attempts = ref 0

    (handleGetSummaryWith
        jobs
        (fun () -> Ok [ d ])
        (fun () -> Ok "chave")
        (fun _ ->
            incr attempts
            // Outro request registrou o job entre a decisão e o TryAdd.
            jobs.[d.id] <- Running NewsSummary.Summarizing
            false)
        ctx)
        .GetAwaiter()
        .GetResult()

    let status, body = readSummaryResponse ctx
    Assert.Equal(200, status)
    use doc = JsonDocument.Parse(body)
    Assert.Equal("summarizing", doc.RootElement.GetProperty("status").GetString())
    Assert.Equal(1, !attempts)

[<Fact>]
let ``handleGetSummaryWith responde 502 para job falho`` () =
    let jobs = freshJobs ()
    let d = summaryDetail 316 ""
    jobs.[d.id] <- Failed(GenerationError "LLM caiu")
    let ctx = summaryCtx (string d.id)

    (handleGetSummaryWith
        jobs
        (fun () -> Ok [ d ])
        (fun () -> Ok "chave")
        (fun _ -> failwith "não deve iniciar")
        ctx)
        .GetAwaiter()
        .GetResult()

    let status, body = readSummaryResponse ctx
    Assert.Equal(502, status)
    use doc = JsonDocument.Parse(body)
    Assert.Equal("LLM caiu", doc.RootElement.GetProperty("error").GetString())

[<Fact>]
let ``summaryStepPayload serializa status e mensagem corretos`` () =
    let payloadFor step =
        use doc = JsonDocument.Parse(JsonSerializer.Serialize(summaryStepPayload step))
        doc.RootElement.GetProperty("status").GetString(),
        doc.RootElement.GetProperty("message").GetString()

    Assert.Equal(("fetching", "Buscando o conteúdo do comunicado…"), payloadFor NewsSummary.Fetching)
    Assert.Equal(("summarizing", "Gerando o resumo com IA…"), payloadFor NewsSummary.Summarizing)
```

## Manter sem alteração

- `handleGetSummary returns cached summary without calling LLM`
- `handleGetSummary returns 400 for invalid id`
- `handleGetSummary returns 404 for unknown id`
- `handleGetSummary returns 500 when uncached and no API key`

## Critério de aceite

- `dotnet test B3News.Tests --filter "FullyQualifiedName~ApiTests"` verde.
- Nenhum teste usa `Async.Sleep` para sincronizar (usa eventos/countdowns).
