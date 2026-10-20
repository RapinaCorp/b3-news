module B3News.Tests.ApiTests

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open System.Text.Json
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Moq
open Xunit
open Api
open Details

type TestQueryCollection(dict: IDictionary<string, string>) =
    let values =
        let d = new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()
        for kvp in dict do
            d.Add(kvp.Key, Microsoft.Extensions.Primitives.StringValues(kvp.Value))
        d :> IDictionary<_, _>

    interface IQueryCollection with
        member this.Item
            with get (key: string) : Microsoft.Extensions.Primitives.StringValues =
                if (values :> IDictionary<_, _>).ContainsKey(key) then
                    (values :> IDictionary<_, _>).[key]
                else
                    Microsoft.Extensions.Primitives.StringValues()

        member this.ContainsKey(key: string) = (values :> IDictionary<_, _>).ContainsKey(key)
        member this.Count = (values :> IDictionary<_, _>).Count

        member this.GetEnumerator() = (values :> IDictionary<_, _>).GetEnumerator()

        member this.Keys = (values :> IDictionary<_, _>).Keys

        member this.TryGetValue(key: string, value: byref<Microsoft.Extensions.Primitives.StringValues>) =
            let success, v = (values :> IDictionary<_, _>).TryGetValue(key)
            value <- v
            success

    interface System.Collections.IEnumerable with
        member this.GetEnumerator() = (values :> System.Collections.IEnumerable).GetEnumerator()

let createMockQuery (dict: IDictionary<string, string>) : IQueryCollection =
    new TestQueryCollection(dict) :> IQueryCollection

[<Fact>]
let ``Test parseQuery with all parameters`` () =
    let queryDict =
        dict [ "codes", "PETR4,VALE3"
               "date", "2024-01-01 - 2024-01-31"
               "filename", "relatorio"
               "reportType", "Fato Relevante"
               "sortBy", "code"
               "sortDirection", "asc"
               "page", "2"
               "pageSize", "50"
               "clientRecordCount", "100"
               "updateRecords", "true" ]
    let query = createMockQuery queryDict
    let result = Api.parseQuery query
    Assert.Equal<string list>([ "PETR4"; "VALE3" ], result.Codes)
    Assert.Equal(("2024-01-01", "2024-01-31"), result.Dates)
    Assert.Equal("relatorio", result.Filename)
    Assert.Equal("Fato Relevante", result.ReportType)
    Assert.Equal(DetailsQuery.SortField.ByTicker, result.SortBy)
    Assert.Equal(DetailsQuery.SortDirection.Asc, result.SortDirection)
    Assert.Equal(2, result.Page)
    Assert.Equal(50, result.PageSize)
    Assert.Equal(Some 100, result.ClientRecordCount)
    Assert.True(result.UpdateRecords)

[<Fact>]
let ``Test parseQuery with all parameters except clientRecordCount`` () =
    let queryDict =
        dict [ "codes", "PETR4,VALE3"
               "date", "2024-01-01 - 2024-01-31"
               "filename", "relatorio"
               "reportType", "Fato Relevante"
               "sortBy", "code"
               "sortDirection", "asc"
               "page", "2"
               "pageSize", "50"
               "updateRecords", "true" ]
    let query = createMockQuery queryDict
    let result = Api.parseQuery query
    Assert.Equal<string list>([ "PETR4"; "VALE3" ], result.Codes)
    Assert.Equal(("2024-01-01", "2024-01-31"), result.Dates)
    Assert.Equal("relatorio", result.Filename)
    Assert.Equal("Fato Relevante", result.ReportType)
    Assert.Equal(DetailsQuery.SortField.ByTicker, result.SortBy)
    Assert.Equal(DetailsQuery.SortDirection.Asc, result.SortDirection)
    Assert.Equal(2, result.Page)
    Assert.Equal(50, result.PageSize)
    Assert.Equal(None, result.ClientRecordCount)
    Assert.True(result.UpdateRecords)

[<Fact>]
let ``Test parseQuery with default values`` () =
    let queryDict = dict []
    let query = createMockQuery queryDict
    let result = Api.parseQuery query
    Assert.Empty(result.Codes)
    Assert.Equal(("", ""), result.Dates)
    Assert.Empty(result.Filename)
    Assert.Empty(result.ReportType)
    Assert.Equal(DetailsQuery.SortField.ByDate, result.SortBy)
    Assert.Equal(DetailsQuery.SortDirection.Desc, result.SortDirection)
    Assert.Equal(1, result.Page)
    Assert.Equal(20, result.PageSize)
    Assert.Equal(None, result.ClientRecordCount)
    Assert.False(result.UpdateRecords)

/// Isolated CWD with a `reports.json` for handlers that resolve
/// `Config.reportsJson` relative to the current directory.
/// Serialized with a lock + try/finally; the only other CWD-dependent suite
/// is `IntegrationTests`, which is excluded from standard unit runs.
let private cwdLock = obj ()

let private withTempReports (items: NewsDetail list) (f: unit -> unit) =
    lock cwdLock (fun () ->
        let prev = Directory.GetCurrentDirectory()
        let dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory dir |> ignore
        try
            Directory.SetCurrentDirectory dir
            File.WriteAllText("reports.json", JsonSerializer.Serialize(items))
            f ()
        finally
            Directory.SetCurrentDirectory prev
            Directory.Delete(dir, true))

let private withRawTempReports (json: string) (f: unit -> unit) =
    lock cwdLock (fun () ->
        let prev = Directory.GetCurrentDirectory()
        let dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory dir |> ignore
        try
            Directory.SetCurrentDirectory dir
            File.WriteAllText("reports.json", json)
            f ()
        finally
            Directory.SetCurrentDirectory prev
            Directory.Delete(dir, true))

let private withNoLlmKey (f: unit -> unit) =
    let vars = [ Config.llmApiKeyEnv; Config.openaiApiKeyEnv; Config.nvidiaApiKeyEnv ]
    let prev = vars |> List.map (fun v -> v, Environment.GetEnvironmentVariable v)
    try
        for v in vars do
            Environment.SetEnvironmentVariable(v, null)
        f ()
    finally
        for v, value in prev do
            Environment.SetEnvironmentVariable(v, value)

let private summaryDetail id summary =
    { id = id
      ticker = "PETR"
      dateTime = "2024-01-15 10:00:00"
      filename = sprintf "file%d.pdf" id
      reportType = "Fato Relevante"
      url = sprintf "http://example.com/%d" id
      summary = summary }

let private summaryCtx (id: string) =
    let ctx = DefaultHttpContext()
    let routeValues = RouteValueDictionary()
    routeValues.["id"] <- id
    ctx.Request.RouteValues <- routeValues
    ctx.Response.Body <- new MemoryStream()
    ctx

let private readSummaryResponse (ctx: DefaultHttpContext) =
    ctx.Response.Body.Seek(0L, SeekOrigin.Begin) |> ignore
    // leaveOpen: o mesmo ctx pode receber mais de um handler (ex.: serve-once).
    use sr = new StreamReader(ctx.Response.Body, Text.Encoding.UTF8, true, 1024, true)
    ctx.Response.StatusCode, sr.ReadToEnd()

let private runSummaryHandler (ctx: DefaultHttpContext) =
    (handleGetSummary ctx).GetAwaiter().GetResult()
    readSummaryResponse ctx

[<Fact>]
let ``handleGetSummary returns cached summary without calling LLM`` () =
    withTempReports [ summaryDetail 1 "resumo em cache"; summaryDetail 2 "" ] (fun () ->
        let status, body = runSummaryHandler (summaryCtx "1")
        Assert.Equal(200, status)
        use doc = JsonDocument.Parse(body)
        Assert.Equal("resumo em cache", doc.RootElement.GetProperty("summary").GetString()))

[<Fact>]
let ``handleGetSummary returns 400 for invalid id`` () =
    withTempReports [ summaryDetail 1 "resumo" ] (fun () ->
        let status, _ = runSummaryHandler (summaryCtx "abc")
        Assert.Equal(400, status))

[<Fact>]
let ``handleGetSummary returns 404 for unknown id`` () =
    withTempReports [ summaryDetail 1 "resumo" ] (fun () ->
        let status, body = runSummaryHandler (summaryCtx "999")
        Assert.Equal(404, status)
        use doc = JsonDocument.Parse(body)
        Assert.False(String.IsNullOrWhiteSpace(doc.RootElement.GetProperty("error").GetString())))

[<Fact>]
let ``handleGetSummary returns 500 when uncached and no API key`` () =
    withNoLlmKey (fun () ->
        withRawTempReports
            """[{"id":1,"ticker":"PETR","dateTime":"2024-01-15 10:00:00","filename":"file1.pdf","reportType":"Fato Relevante","url":"http://example.com/1"}]"""
            (fun () ->
                let status, _ = runSummaryHandler (summaryCtx "1")
                Assert.Equal(500, status)))

let private freshJobs () = ConcurrentDictionary<int, SummaryState>()

let private runJob jobs read generate persist detail =
    runSummaryJob jobs read generate persist detail |> Async.RunSynchronously

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
    let gate = new System.Threading.ManualResetEventSlim(false)
    let doneEvent = new System.Threading.ManualResetEventSlim(false)
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
    let gate = new System.Threading.ManualResetEventSlim(false)
    let bothStarted = new System.Threading.CountdownEvent 2
    let allDone = new System.Threading.CountdownEvent 2
    let calls = ref 0

    let generate (d: NewsDetail) (_: NewsSummary.SummaryStep -> unit) =
        async {
            System.Threading.Interlocked.Increment(calls) |> ignore
            bothStarted.Signal() |> ignore
            gate.Wait()
            allDone.Signal() |> ignore
            return Ok(sprintf "resumo %d" d.id)
        }

    let persist (_: int) (_: string) = Ok()

    Assert.True(startSummaryJob jobs (fun () -> Ok [ d1 ]) generate persist d1)
    Assert.True(startSummaryJob jobs (fun () -> Ok [ d2 ]) generate persist d2)

    // Ambos precisam estar gerando ao mesmo tempo para destravar o countdown.
    Assert.True(bothStarted.Wait(TimeSpan.FromSeconds 5.0))
    gate.Set()
    Assert.True(allDone.Wait(TimeSpan.FromSeconds 5.0))
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
let ``handleGetSummaryWith responde 500 quando todas as tentativas perdem a corrida`` () =
    let jobs = freshJobs ()
    let d = summaryDetail 317 ""
    let ctx = summaryCtx (string d.id)
    let attempts = ref 0

    (handleGetSummaryWith
        jobs
        (fun () -> Ok [ d ])
        (fun () -> Ok "chave")
        (fun _ ->
            incr attempts
            false)
        ctx)
        .GetAwaiter()
        .GetResult()

    let status, body = readSummaryResponse ctx
    Assert.Equal(500, status)
    use doc = JsonDocument.Parse(body)
    Assert.Equal("não foi possível iniciar o resumo (concorrência)", doc.RootElement.GetProperty("error").GetString())
    Assert.Equal(4, !attempts)

[<Fact>]
let ``handleGetSummaryWith serve o resumo sem cache uma única vez`` () =
    let jobs = freshJobs ()
    let d = summaryDetail 318 ""
    jobs.[d.id] <- Succeeded "resumo sem cache"
    let startCalls = ref 0

    let call () =
        let ctx = summaryCtx (string d.id)

        (handleGetSummaryWith
            jobs
            (fun () -> Ok [ d ])
            (fun () -> Ok "chave")
            (fun _ ->
                incr startCalls
                true)
            ctx)
            .GetAwaiter()
            .GetResult()

        readSummaryResponse ctx

    let status1, body1 = call ()
    Assert.Equal(200, status1)
    use doc1 = JsonDocument.Parse(body1)
    Assert.Equal("resumo sem cache", doc1.RootElement.GetProperty("summary").GetString())
    Assert.False(jobs.ContainsKey d.id)

    // A entrada saiu no primeiro GET: o segundo volta a decidir (e inicia job).
    let status2, body2 = call ()
    Assert.Equal(200, status2)
    use doc2 = JsonDocument.Parse(body2)
    Assert.Equal("fetching", doc2.RootElement.GetProperty("status").GetString())
    Assert.Equal(1, !startCalls)

[<Fact>]
let ``summaryStepPayload serializa status e mensagem corretos`` () =
    let payloadFor step =
        use doc = JsonDocument.Parse(JsonSerializer.Serialize(summaryStepPayload step))
        doc.RootElement.GetProperty("status").GetString(),
        doc.RootElement.GetProperty("message").GetString()

    Assert.Equal(("fetching", "Buscando o conteúdo do comunicado…"), payloadFor NewsSummary.Fetching)
    Assert.Equal(("summarizing", "Gerando o resumo com IA…"), payloadFor NewsSummary.Summarizing)
