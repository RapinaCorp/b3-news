# Passo 1 — Backend core: `SummaryStep`/`onStep` e máquina de estados

## `NewsSummary.fs`

Adicionar o tipo `SummaryStep` e o parâmetro `onStep` em `summarizeDetail`
(antes de `detail`, último parâmetro):

```fsharp
/// Etapa corrente do pipeline de resumo, reportada a quem inicia o job.
type SummaryStep =
    | Fetching
    | Summarizing

/// Extrai o conteúdo do link do comunicado e gera o resumo em texto.
/// `onStep` é chamado antes da extração (`Fetching`) e antes da chamada à
/// LLM (`Summarizing`) — quem inicia o job decide o que fazer com isso.
let summarizeDetail
    (endpoint: string)
    (apiKey: string)
    (model: string)
    (maxRetries: int)
    (maxChars: int)
    (disableReasoning: bool)
    (onStep: SummaryStep -> unit)
    (detail: NewsDetail)
    : Async<Result<string, string>> =
    async {
        onStep Fetching
        let! extracted = ContentExtractor.extractText detail.url maxChars
        match extracted with
        | Error err -> return Error(sprintf "Falha ao extrair conteúdo: %s" err)
        | Ok content ->
            let userText =
                sprintf "Título: %s\nData: %s\n\n%s" detail.reportType detail.dateTime content.Text
            onStep Summarizing
            let! summary =
                OpenAIClient.summarize
                    endpoint
                    apiKey
                    model
                    (individualSystemPrompt disableReasoning)
                    userText
                    detail.ticker
                    maxRetries
                    disableReasoning
            match summary with
            | Error err -> return Error(sprintf "Falha ao gerar resumo: %s" err)
            | Ok text -> return Ok text
    }
```

`SummaryStep` vive em `NewsSummary` (carregado antes de `Api` no
`B3News.fsproj`), então `Api.fs` usa qualificado (`NewsSummary.Fetching`).
Nenhuma mudança no `.fsproj`.

## `Api.fs` — tipos e funções de estado

Substituir o bloco atual de `SummaryErr`/`summaryGates`/`getOrGenerateSummary`/
`handleGetSummary` (linhas ~203-307) por: `SummaryErr` (mantido idêntico), os
novos tipos e as funções abaixo (o handler em si fica para o passo 2).

```fsharp
/// Estado em memória de um job de resumo (não persistido; some no restart).
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

/// Payload JSON das respostas de progresso.
let summaryStepPayload (step: NewsSummary.SummaryStep) : obj =
    match step with
    | NewsSummary.Fetching ->
        box {| status = "fetching"; message = "Buscando o conteúdo do comunicado…" |}
    | NewsSummary.Summarizing ->
        box {| status = "summarizing"; message = "Gerando o resumo com IA…" |}

/// Uma entrada por id de job já pedido; `TryAdd` é o gate (um job por id).
let private summaryJobs = ConcurrentDictionary<int, SummaryState>()

/// Decide a resposta de uma requisição SEM iniciar job: consulta o registry
/// primeiro (poll não toca o disco), depois o store, depois a chave.
let decideSummaryRequest
    (jobs: ConcurrentDictionary<int, SummaryState>)
    (read: unit -> Result<Details.NewsDetail list, string>)
    (apiKey: unit -> Result<string, string>)
    (id: int)
    : SummaryDecision =
    match jobs.TryGetValue id with
    | true, Running step -> ServeStep step
    | true, Succeeded summary ->
        // Resposta idempotente: calculada do estado lido; a remoção é
        // melhor esforço (outra requisição pode ter removido antes).
        jobs.TryRemove id |> ignore
        ServeSummary summary
    | true, Failed err ->
        jobs.TryRemove id |> ignore
        FailSummary err
    | false, _ ->
        match read () with
        | Error err -> FailSummary(StoreError err)
        | Ok details ->
            match details |> List.tryFind (fun d -> d.id = id) with
            | None -> SummaryNotFound
            | Some d when not (System.String.IsNullOrWhiteSpace d.summary) -> ServeSummary d.summary
            | Some d ->
                match apiKey () with
                | Error err -> MissingKey err
                | Ok _ -> StartRequested d

/// Executa o pipeline do job. O chamador já registrou `Running Fetching`.
/// Re-lê o store antes de gerar (preserva a garantia do gate antigo), roda
/// `generate` reportando etapas via `onStep`, persiste e então transita o
/// estado. `try/with` total: nunca deixa `Running` preso.
let runSummaryJob
    (jobs: ConcurrentDictionary<int, SummaryState>)
    (read: unit -> Result<Details.NewsDetail list, string>)
    (generate: Details.NewsDetail -> (NewsSummary.SummaryStep -> unit) -> Async<Result<string, string>>)
    (persist: int -> string -> Result<unit, string>)
    (detail: Details.NewsDetail)
    : Async<unit> =
    async {
        try
            match read () with
            | Error err -> jobs.[detail.id] <- Failed(StoreError err)
            | Ok details ->
                match details |> List.tryFind (fun d -> d.id = detail.id) with
                | Some d when not (System.String.IsNullOrWhiteSpace d.summary) ->
                    // Já persistido enquanto este job subia: nada a gerar.
                    jobs.TryRemove detail.id |> ignore
                | _ ->
                    let! result =
                        generate detail (fun step -> jobs.[detail.id] <- Running step)

                    match result with
                    | Error err -> jobs.[detail.id] <- Failed(GenerationError err)
                    | Ok summary ->
                        // Persist ANTES da transição terminal: um poll não pode
                        // redespachar enquanto este pipeline ainda escreve.
                        match persist detail.id summary with
                        | Ok () -> jobs.TryRemove detail.id |> ignore
                        | Error err ->
                            Log.warning (sprintf "falha ao salvar resumo [id=%d]: %s" detail.id err)
                            jobs.[detail.id] <- Succeeded summary
        with ex ->
            Log.error (sprintf "erro inesperado no job de resumo [id=%d]: %s" detail.id ex.Message)
            jobs.[detail.id] <- Failed(GenerationError ex.Message)
    }

/// Inicia o job em background se ainda não houver um para o id.
/// `false` = perdeu a corrida do `TryAdd` (outro job já está registrado).
let startSummaryJob
    (jobs: ConcurrentDictionary<int, SummaryState>)
    (read: unit -> Result<Details.NewsDetail list, string>)
    (generate: Details.NewsDetail -> (NewsSummary.SummaryStep -> unit) -> Async<Result<string, string>>)
    (persist: int -> string -> Result<unit, string>)
    (detail: Details.NewsDetail)
    : bool =
    if jobs.TryAdd(detail.id, Running NewsSummary.Fetching) then
        // Async.Start agenda no threadpool: a request retorna na hora e o
        // pipeline nunca roda inline na thread da request.
        Async.Start(
            async {
                try
                    do! runSummaryJob jobs read generate persist detail

                    try
                        Log.info (sprintf "job de resumo concluído [id=%d]" detail.id)
                    with _ ->
                        ()
                with ex ->
                    jobs.[detail.id] <- Failed(GenerationError ex.Message)

                    try
                        Log.error (sprintf "job de resumo falhou inesperadamente [id=%d]: %s" detail.id ex.Message)
                    with _ ->
                        ()
            }
        )

        // Log depois do Async.Start: uma falha de log não pode deixar a
        // entrada presa em Running.
        try
            Log.info (sprintf "iniciando resumo [id=%d]" detail.id)
        with _ ->
            ()

        true
    else
        false
```

Notas:

- `SummaryErr` continua exatamente como hoje (`StoreError | GenerationError`);
  o pipeline usa `persist` com `Result<unit,string>` (é
  `JsonStore.updateSummary Config.reportsJson`), então o warning de persist
  falho passa a ser do pipeline (antes ficava no handler).
- `Async.Start` (não `task { do! async }`/`Async.StartAsTask`): tanto `task`
  quanto `Async` iniciam sincronamente até o primeiro yield real — com o
  `generate` bloqueante dos testes, `startSummaryJob` rodava o pipeline inline
  e travava a thread chamadora. `Async.Start` enfileira direto no threadpool.
- Transições de estado são escritas antes dos `Log.*` e os logs terminais são
  guardados com `try/with` (revisão pós-implementação): um throw de logging
  não pode deixar a entrada presa em `Running` nem sobrescrever uma entrada
  mais nova.

## Ordem e build

Os passos 1 e 2 fazem parte da mesma mudança de compilação: enquanto o handler
antigo ainda existir, ele referencia `NewsSummary.summarizeDetail` sem o novo
parâmetro e o projeto não compila. Aplicar 1 e 2 juntos e então rodar
`dotnet build`.
