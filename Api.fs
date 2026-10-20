module Api

open System
open System.Collections.Concurrent
open System.IO
open System.Threading
open System.Text.Json
open Microsoft.AspNetCore.Http
open System.Net
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open System.Threading.Tasks
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Caching.Memory
open Microsoft.Extensions.FileProviders

type QueryParams =
    { Codes: string list
      Dates: string * string
      Filename: string
      ReportType: string
      SortBy: DetailsQuery.SortField
      SortDirection: DetailsQuery.SortDirection
      Page: int
      PageSize: int
      ClientRecordCount: int option
      UpdateRecords: bool }

let parseQuery (query: IQueryCollection) =
    let getStringValue key defaultValue =
        if query.ContainsKey(key) then
            query.[key].ToString()
        else
            defaultValue

    let getIntValue key defaultValue =
        if query.ContainsKey(key) then
            match System.Int32.TryParse(query.[key].ToString()) with
            | true, value -> value
            | false, _ -> defaultValue
        else
            defaultValue

    let getOptIntValue key =
        if query.ContainsKey(key) then
            match System.Int32.TryParse(query.[key].ToString()) with
            | true, value when value > 0 -> Some value
            | _ -> None

        else
            None

    let getBoolValue key =
        if query.ContainsKey(key) then
            match query.[key].ToString().ToLowerInvariant() with
            | "true"
            | "1"
            | "yes" -> true
            | _ -> false
        else
            false

    let getSortField =
        match getStringValue "sortBy" "dateTime" with
        | "code" -> DetailsQuery.SortField.ByTicker
        | "type" -> DetailsQuery.SortField.ByReportType
        | "filename" -> DetailsQuery.SortField.ByFilename
        | _ -> DetailsQuery.SortField.ByDate

    let getSortDirection =
        match getStringValue "sortDirection" "desc" with
        | "asc" -> DetailsQuery.SortDirection.Asc
        | _ -> DetailsQuery.SortDirection.Desc

    { Codes =
        getStringValue "codes" ""
        |> fun s -> if s = "" then [] else s.Split(',') |> Array.toList

      Dates =
        // Expecting date format "YYYY-MM-DD - YYYY-MM-DD" or just "YYYY-MM-DD"
        match getStringValue "date" "" with
        | "" -> "", ""
        | s ->
            s.Split([| " - " |], StringSplitOptions.None)
            |> fun arr -> arr.[0].Trim(), (if arr.Length > 1 then arr.[1].Trim() else arr.[0].Trim())

      Filename = getStringValue "filename" ""
      ReportType = getStringValue "reportType" ""
      SortBy = getSortField
      SortDirection = getSortDirection
      Page = getIntValue "page" 1
      PageSize = getIntValue "pageSize" 20
      ClientRecordCount = getOptIntValue "clientRecordCount"
      UpdateRecords = getBoolValue "updateRecords" }

// Response helpers
let private writeJsonResponse (ctx: HttpContext) statusCode data =
    async {
        ctx.Response.StatusCode <- int statusCode
        ctx.Response.ContentType <- "application/json"
        let json =
            JsonSerializer.Serialize(data, JsonSerializerOptions(WriteIndented = true))
        do! ctx.Response.WriteAsync json |> Async.AwaitTask
    }

let private writeErrorResponse (ctx: HttpContext) statusCode message =
    writeJsonResponse ctx statusCode {| error = message |}

// API handlers
let handleLoadPagedNews (ctx: HttpContext) : Task =
    task {
        try
            let p = parseQuery ctx.Request.Query
            let crit =
                DetailsQuery.newCriteria ()
                |> DetailsQuery.withTickers p.Codes
                |> DetailsQuery.withDateRange (fst p.Dates) (snd p.Dates)
                |> DetailsQuery.withFilename p.Filename
                |> DetailsQuery.withReportType p.ReportType
                |> DetailsQuery.withPagination p.Page p.PageSize
                |> DetailsQuery.withSortBy p.SortBy p.SortDirection
                |> (fun crit' ->
                    if p.UpdateRecords then
                        crit'
                    else
                        DetailsQuery.withClientRecordCount p.ClientRecordCount crit')

            match JsonStore.loadPagedNewsWithCriteria Config.reportsJson crit with
            | Error err -> do! writeErrorResponse ctx HttpStatusCode.BadRequest err
            | Ok r ->
                let response =
                    {| news = r.Items
                       totalPages = r.TotalPages
                       totalRecords = r.TotalRecords |}
                do! writeJsonResponse ctx HttpStatusCode.OK response
        with ex ->
            do! writeErrorResponse ctx HttpStatusCode.InternalServerError ex.Message
    }

let handleNumberOfReports (cache: IMemoryCache) (ctx: HttpContext) : Task =
    task {
        try
            if not <| File.Exists(Config.reportsJson) then
                do! writeErrorResponse ctx HttpStatusCode.NotFound "reports.json not found"
            else
                let p = parseQuery ctx.Request.Query
                // Create a cache key based on the query parameters
                let cacheKey = sprintf "count-%A-%A-%s-%s" p.Codes p.Dates p.Filename p.ReportType

                let! count =
                    cache.GetOrCreateAsync(
                        cacheKey,
                        (fun (entry: ICacheEntry) ->
                            task {
                                let fileProvider = new PhysicalFileProvider(Directory.GetCurrentDirectory())
                                entry.AddExpirationToken(fileProvider.Watch Config.reportsJson) |> ignore
                                entry.SetSlidingExpiration(TimeSpan.FromMinutes 10.0) |> ignore

                                let criteria =
                                    DetailsQuery.newCriteria ()
                                    |> DetailsQuery.withTickers p.Codes
                                    |> DetailsQuery.withDateRange (fst p.Dates) (snd p.Dates)
                                    |> DetailsQuery.withFilename p.Filename
                                    |> DetailsQuery.withReportType p.ReportType
                                    |> DetailsQuery.withPagination 1 9999999 // Get all records for counting

                                match JsonStore.loadPagedNewsWithCriteria Config.reportsJson criteria with
                                | Ok r -> return r.TotalRecords
                                | Error _ -> return 0 // On error, return 0, cache will expire shortly
                            })
                    )
                    |> Async.AwaitTask

                do! writeJsonResponse ctx HttpStatusCode.OK {| totalRecords = count |}
        with ex ->
            do! writeErrorResponse ctx HttpStatusCode.InternalServerError ex.Message
    }

let handlePdfRequest (ctx: HttpContext) : Task =
    task {
        let id = ctx.Request.RouteValues["id"] :?> string
        match System.Int32.TryParse id with
        | false, _ ->
            ctx.Response.StatusCode <- 400
            do! ctx.Response.WriteAsync("ID inválido")
            return ()
        | true, idInt ->
            let! fetched = Download.fetchBytes (Download.Enet idInt) |> Async.StartAsTask
            match fetched with
            | Error err ->
                ctx.Response.StatusCode <- Download.httpStatusForFetchErr err
                do! ctx.Response.WriteAsync(Download.describeFetchErr err)
                return ()
            | Ok (pdfBytes, _) ->
                ctx.Response.StatusCode <- 200
                ctx.Response.ContentType <- "application/pdf"
                ctx.Response.Headers.["Content-Disposition"] <- $"""inline; filename={id}.pdf"""
                do! ctx.Response.Body.WriteAsync(pdfBytes, 0, pdfBytes.Length)
                return ()
    }

/// Error surface of a summary job: store failures vs generation
/// failures map to different HTTP statuses (500 vs 502).
type SummaryErr =
    | StoreError of string
    | GenerationError of string

/// In-memory state of one summary job. Terminal states are served once to
/// the requester that observes them and then removed (see
/// `decideSummaryRequest`), so a poll never re-dispatches while the owning
/// pipeline is still writing. Not persisted: a restart loses running jobs.
type SummaryState =
    | Running of NewsSummary.SummaryStep
    | Succeeded of summary: string   // persist failed: serve once, then remove
    | Failed of SummaryErr           // next read replies the error and removes

/// Reply decision for one summary request; `StartRequested` means the
/// caller must claim the job (`startSummaryJob`) and respond.
type SummaryDecision =
    | ServeSummary of string
    | ServeStep of NewsSummary.SummaryStep
    | FailSummary of SummaryErr
    | MissingKey of string
    | SummaryNotFound
    | StartRequested of Details.NewsDetail

/// JSON payload of the progress replies.
let summaryStepPayload (step: NewsSummary.SummaryStep) : obj =
    match step with
    | NewsSummary.Fetching ->
        box {| status = "fetching"; message = "Buscando o conteúdo do comunicado…" |}
    | NewsSummary.Summarizing ->
        box {| status = "summarizing"; message = "Gerando o resumo com IA…" |}

/// One entry per id ever requested; `TryAdd` is the one-job-per-id gate.
let private summaryJobs = ConcurrentDictionary<int, SummaryState>()

/// Decide the reply for one request WITHOUT starting a job: the registry is
/// probed first so polls do not re-read `reports.json`, then the store, then
/// the API key. Terminal entries are answered from the state read and
/// removed best-effort — replies are idempotent, so concurrent polls all get
/// the terminal answer and nobody re-dispatches.
let decideSummaryRequest
    (jobs: ConcurrentDictionary<int, SummaryState>)
    (read: unit -> Result<Details.NewsDetail list, string>)
    (apiKey: unit -> Result<string, string>)
    (id: int)
    : SummaryDecision =
    match jobs.TryGetValue id with
    | true, Running step -> ServeStep step
    | true, Succeeded summary ->
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

/// Run one job's pipeline. The caller has already claimed the entry
/// (`Running Fetching`). Re-reads the store before generating (another
/// request may have persisted while this job was starting), reports steps
/// through `onStep`, persists and only then transitions the state — a poll
/// can never re-dispatch while this pipeline is still writing. The total
/// `try/with` guarantees no entry is left `Running` on an unexpected throw.
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
                    // Already persisted while this job was starting.
                    jobs.TryRemove detail.id |> ignore
                | _ ->
                    let! result =
                        generate detail (fun step -> jobs.[detail.id] <- Running step)

                    match result with
                    | Error err -> jobs.[detail.id] <- Failed(GenerationError err)
                    | Ok summary ->
                        match persist detail.id summary with
                        | Ok () -> jobs.TryRemove detail.id |> ignore
                        | Error err ->
                            // Serve the generated summary once instead of
                            // wasting it; it just is not cached on disk.
                            jobs.[detail.id] <- Succeeded summary

                            try
                                Log.warning (sprintf "falha ao salvar resumo [id=%d]: %s" detail.id err)
                            with _ ->
                                ()
        with ex ->
            jobs.[detail.id] <- Failed(GenerationError ex.Message)

            try
                Log.error (sprintf "erro inesperado no job de resumo [id=%d]: %s" detail.id ex.Message)
            with _ ->
                ()
    }

/// Start the background job for `detail` unless another one is already
/// registered. `false` means the `TryAdd` race was lost.
let startSummaryJob
    (jobs: ConcurrentDictionary<int, SummaryState>)
    (read: unit -> Result<Details.NewsDetail list, string>)
    (generate: Details.NewsDetail -> (NewsSummary.SummaryStep -> unit) -> Async<Result<string, string>>)
    (persist: int -> string -> Result<unit, string>)
    (detail: Details.NewsDetail)
    : bool =
    if jobs.TryAdd(detail.id, Running NewsSummary.Fetching) then
        // Async.Start queues on the threadpool: the request thread returns
        // immediately and the pipeline never runs inline on it.
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

        // Log after Async.Start so a logging failure cannot strand the entry.
        try
            Log.info (sprintf "iniciando resumo [id=%d]" detail.id)
        with _ ->
            ()

        true
    else
        false

/// Summary handler with injected dependencies (same pattern as
/// `handleNumberOfReports memoryCache`) so tests can exercise the state
/// machine without the real store or LLM. A lost `TryAdd` race re-dispatches
/// up to three times before failing.
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

            // A lost `TryAdd` race re-dispatches: the winner's state should
            // be visible on the next probe.
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

let handleGetSummary (ctx: HttpContext) : Task =
    let read () = JsonStore.tryRead Config.reportsJson

    let generate detail onStep =
        // Runs inside the job's try: a vanishing key surfaces as a failed
        // job instead of an unhandled exception.
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

let private embeddedHtml: Map<string, string> =
    let asm = System.Reflection.Assembly.GetExecutingAssembly()

    [ "report_viewer.html"; "report_viewer_legacy.html" ]
    |> List.map (fun name ->
        use stream = asm.GetManifestResourceStream($"B3News.{name}")

        match stream with
        | null -> failwithf "Embedded resource 'B3News.%s' not found" name
        | s ->
            use reader = new System.IO.StreamReader(s)
            name, reader.ReadToEnd())
    |> Map.ofList

let handleHtmlFile (resourceName: string) (ctx: HttpContext) : Task =
    task {
        match Map.tryFind resourceName embeddedHtml with
        | Some html ->
            ctx.Response.StatusCode <- 200
            ctx.Response.ContentType <- "text/html; charset=utf-8"
            do! ctx.Response.WriteAsync(html)
        | None ->
            ctx.Response.StatusCode <- 404
            do! ctx.Response.WriteAsync($"'{resourceName}' not found")
    }

let configureRoutes (app: WebApplication) memoryCache =
    do
        app.MapGet("/pdf/{id}.pdf", handlePdfRequest) |> ignore
        app.MapGet("/api/news/count", handleNumberOfReports memoryCache) |> ignore
        app.MapGet("/api/news", handleLoadPagedNews) |> ignore
        app.MapGet("/api/news/{id}/summary", handleGetSummary) |> ignore
        app.MapGet("/legacy", handleHtmlFile "report_viewer_legacy.html") |> ignore
        app.MapGet("/", handleHtmlFile "report_viewer.html") |> ignore

let startApiServer (args: string array) =
    // Verificar se a variável de ambiente do provedor de LLM está definida
    match Config.llmApiKey () with 
    | Ok _ -> ()
    | Error msg -> Log.warning $"{msg}\nA API de resumo não funcionará sem uma chave válida."

    let builder = WebApplication.CreateBuilder args
    do builder.Configuration.AddJsonFile Config.configFileName |> ignore
    do builder.Services.AddControllers() |> ignore
    do builder.Services.AddMemoryCache() |> ignore

    let app = builder.Build()

    do
        if app.Environment.IsDevelopment() then
            app.UseDeveloperExceptionPage() |> ignore

    let memoryCache = app.Services.GetRequiredService<IMemoryCache>()
    do configureRoutes app memoryCache

    do app.Run()
    0
