module Api

open System
open System.IO
open System.Text.Json
open Microsoft.AspNetCore.Http
open System.Net
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open System.Net.Http
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

let isPdfContent (pdfBytes: byte array) : bool =
    // Check if it starts with PDF signature
    if pdfBytes.Length < 4 then
        false
    else
        // PDF files start with "%PDF"
        pdfBytes.[0] = 0x25uy
        && pdfBytes.[1] = 0x50uy
        && pdfBytes.[2] = 0x44uy
        && pdfBytes.[3] = 0x46uy

let handlePdfRequest (ctx: HttpContext) : Task =
    task {
        let id = ctx.Request.RouteValues["id"] :?> string
        use client = new HttpClient()
        let jsonBody =
            $"""{{ "codigoInstituicao": "2", "numeroProtocolo": "{id}", "token": "", "versaoCaptcha": "" }}"""
        let remoteUrl =
            "https://www.rad.cvm.gov.br/ENET/frmExibirArquivoIPEExterno.aspx/ExibirPDF"
        use content =
            new System.Net.Http.StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json")
        let! postResp = WebServer.postWithRetry client remoteUrl content 3
        match postResp with
        | Error ex ->
            ctx.Response.StatusCode <- 502
            do! ctx.Response.WriteAsync($"Error fetching PDF: {ex.Message}")
            return ()
        | Ok response ->
            if response.IsSuccessStatusCode then
                let! jsonStr = response.Content.ReadAsStringAsync()
                try
                    let doc = System.Text.Json.JsonDocument.Parse(jsonStr)
                    match doc.RootElement.TryGetProperty("d") with
                    | true, dProp ->
                        let base64Pdf = dProp.GetString()
                        if not (System.String.IsNullOrEmpty(base64Pdf)) then
                            try
                                let pdfBytes = System.Convert.FromBase64String(base64Pdf)
                                if not (isPdfContent pdfBytes) then
                                    ctx.Response.StatusCode <- 422 // Unprocessable Entity
                                    do! ctx.Response.WriteAsync("Invalid PDF content received from server.")
                                else
                                    ctx.Response.StatusCode <- 200
                                    ctx.Response.ContentType <- "application/pdf"
                                    ctx.Response.Headers.["Content-Disposition"] <- $"""inline; filename={id}.pdf"""
                                    do! ctx.Response.Body.WriteAsync(pdfBytes, 0, pdfBytes.Length)
                            with
                            | :? System.FormatException ->
                                ctx.Response.StatusCode <- 422
                                do! ctx.Response.WriteAsync("Invalid base64 PDF data received from server.")
                            | ex ->
                                ctx.Response.StatusCode <- 500
                                do! ctx.Response.WriteAsync($"Error processing PDF data: {ex.Message}")
                        else
                            ctx.Response.StatusCode <- 502
                            do! ctx.Response.WriteAsync("PDF data missing in response.")
                    | false, _ ->
                        ctx.Response.StatusCode <- 502
                        do! ctx.Response.WriteAsync("Missing 'd' property in response JSON.")
                with ex ->
                    ctx.Response.StatusCode <- 502
                    ctx.Response.ContentType <- "text/html; charset=utf-8"
                    // do! ctx.Response.WriteAsync($"Error parsing PDF response: {ex.Message}")
                    do! ctx.Response.WriteAsync(jsonStr)
            else
                ctx.Response.StatusCode <- 502
                do! ctx.Response.WriteAsync("Failed to fetch PDF from remote server.")
    }

let handleHtmlFile (filePath: string) (ctx: HttpContext) : Task =
    task {
        try
            if System.IO.File.Exists(filePath) then
                ctx.Response.StatusCode <- 200
                ctx.Response.ContentType <- "text/html; charset=utf-8"
                let! html = System.IO.File.ReadAllTextAsync(filePath)
                do! ctx.Response.WriteAsync(html)
            else
                ctx.Response.StatusCode <- 404
                do! ctx.Response.WriteAsync($"""{filePath} not found""")
        with ex ->
            ctx.Response.StatusCode <- 500
            do! ctx.Response.WriteAsync($"Internal server error: {ex.Message}")
    }

let configureRoutes (app: WebApplication) memoryCache =
    do
        app.MapGet("/pdf/{id}.pdf", handlePdfRequest) |> ignore
        app.MapGet("/api/news/count", handleNumberOfReports memoryCache) |> ignore
        app.MapGet("/api/news", handleLoadPagedNews) |> ignore
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
