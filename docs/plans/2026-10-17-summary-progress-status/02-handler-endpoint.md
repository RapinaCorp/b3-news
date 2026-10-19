# Passo 2 — Handler e composição real

## `handleGetSummaryWith` (deps injetadas)

No lugar do antigo `handleGetSummary` (bloco removido no passo 1), adicionar em
`Api.fs`:

```fsharp
/// Handler do resumo com dependências injetadas (mesmo padrão de
/// `handleNumberOfReports memoryCache`), para os testes exercitarem a máquina
/// de estados sem rede/store reais.
let handleGetSummaryWith
    (jobs: ConcurrentDictionary<int, SummaryState>)
    (read: unit -> Result<Details.NewsDetail list, string>)
    (apiKey: unit -> Result<string, string>)
    (startJob: Details.NewsDetail -> bool)
    (ctx: HttpContext)
    : Task =
    task {
        let idStr = ctx.Request.RouteValues["id"] :?> string

        match System.Int32.TryParse idStr with
        | false, _ ->
            do! writeErrorResponse ctx HttpStatusCode.BadRequest "ID inválido"
        | true, id ->
            let mutable attempts = 0
            let mutable handled = false

            // Perdeu o TryAdd para outro request: re-consulta o registry
            // (normalmente o vencedor já está lá).
            while not handled && attempts < 4 do
                match decideSummaryRequest jobs read apiKey id with
                | ServeSummary summary ->
                    do! writeJsonResponse ctx HttpStatusCode.OK {| summary = summary |}
                    handled <- true
                | ServeStep step ->
                    do! writeJsonResponse ctx HttpStatusCode.OK (summaryStepPayload step)
                    handled <- true
                | FailSummary(StoreError err) ->
                    do! writeErrorResponse ctx HttpStatusCode.InternalServerError err
                    handled <- true
                | FailSummary(GenerationError err) ->
                    do! writeErrorResponse ctx HttpStatusCode.BadGateway err
                    handled <- true
                | MissingKey err ->
                    do! writeErrorResponse ctx HttpStatusCode.InternalServerError err
                    handled <- true
                | SummaryNotFound ->
                    do! writeErrorResponse ctx HttpStatusCode.NotFound "Comunicado não encontrado"
                    handled <- true
                | StartRequested detail ->
                    if startJob detail then
                        do! writeJsonResponse ctx HttpStatusCode.OK (summaryStepPayload NewsSummary.Fetching)
                        handled <- true
                    else
                        attempts <- attempts + 1

            if not handled then
                do! writeErrorResponse
                        ctx
                        HttpStatusCode.InternalServerError
                        "não foi possível iniciar o resumo (concorrência)"
    }
```

## Composição real (o que `configureRoutes` registra)

```fsharp
let handleGetSummary (ctx: HttpContext) : Task =
    let read () = JsonStore.tryRead Config.reportsJson

    let generate detail onStep =
        // Chamado dentro do `try` do pipeline: a chave ausente vira
        // `Failed(GenerationError …)` em vez de derrubar o job.
        let apiKey =
            match Config.llmApiKey () with
            | Ok key -> key
            | Error err -> failwith err

        let settings = Config.readSummarySettings ()

        NewsSummary.summarizeDetail
            settings.Endpoint
            apiKey
            settings.Model
            settings.MaxRetries
            settings.MaxChars
            settings.DisableReasoning
            onStep
            detail

    handleGetSummaryWith
        summaryJobs
        read
        Config.llmApiKey
        (startSummaryJob summaryJobs read generate (JsonStore.updateSummary Config.reportsJson))
        ctx
```

- `summaryJobs` é o `ConcurrentDictionary` privado definido no passo 1.
- `configureRoutes` continua com
  `app.MapGet("/api/news/{id}/summary", handleGetSummary) |> ignore` — nenhuma
  mudança de rota.
- O import `System.Collections.Concurrent` já existe no topo de `Api.fs`.

## Critério de aceite

- `dotnet build` verde após os passos 1+2.
- Primeiro GET de item sem resumo responde 200 `{status:"fetching", …}`.
- GET de item cacheado continua `{summary}`.
- Sem `NVIDIA_API_KEY` e sem job, continua 500 (teste existente).
