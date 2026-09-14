module Api

open System
open System.IO
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

let handleGetSummary (ctx: HttpContext) : Task =
    task {
        let idStr = ctx.Request.RouteValues["id"] :?> string
        match System.Int32.TryParse idStr with
        | false, _ ->
            do! writeErrorResponse ctx HttpStatusCode.BadRequest "ID inválido"
        | true, id ->
            match JsonStore.tryRead Config.reportsJson with
            | Error err ->
                do! writeErrorResponse ctx HttpStatusCode.InternalServerError err
            | Ok details ->
                match details |> List.tryFind (fun d -> d.id = id) with
                | None ->
                    do! writeErrorResponse ctx HttpStatusCode.NotFound "Comunicado não encontrado"
                | Some detail when not (System.String.IsNullOrWhiteSpace detail.summary) ->
                    do! writeJsonResponse ctx HttpStatusCode.OK {| summary = detail.summary |}
                | Some detail ->
                    match Config.nvidiaApiKey () with
                    | Error err ->
                        do! writeErrorResponse ctx HttpStatusCode.InternalServerError err
                    | Ok apiKey ->
                        let settings = Config.readSummarySettings ()
                        let! result =
                            NewsSummary.summarizeDetail
                                apiKey
                                settings.Model
                                settings.MaxRetries
                                settings.MaxChars
                                settings.DisableReasoning
                                detail
                            |> Async.StartAsTask
                        match result with
                        | Error err ->
                            do! writeErrorResponse ctx HttpStatusCode.BadGateway err
                        | Ok summary ->
                            match JsonStore.updateSummary Config.reportsJson id summary with
                            | Error err ->
                                Log.warning (sprintf "falha ao salvar resumo [id=%d]: %s" id err)
                                // Ainda assim retorna o resumo gerado ao usuário,
                                // mesmo que a escrita em disco tenha falhado —
                                // só não fica cacheado para a próxima vez.
                                do! writeJsonResponse ctx HttpStatusCode.OK {| summary = summary |}
                            | Ok () ->
                                do! writeJsonResponse ctx HttpStatusCode.OK {| summary = summary |}
    }

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
