module Api

open System.Text.Json
open Microsoft.AspNetCore.Http
open System.Net
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open System.Net.Http
open System.Threading.Tasks
open Microsoft.Extensions.Configuration

type QueryParams =
    { Codes: string list
      Dates: string * string
      Filename: string
      ReportType: string
      SortBy: DetailsQuery.SortField
      SortDirection: DetailsQuery.SortDirection
      Page: int
      PageSize: int }

let private parseQuery (query: IQueryCollection) =
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
            s.Split('-')
            |> fun arr -> arr.[0].Trim(), (if arr.Length > 1 then arr.[1].Trim() else arr.[0].Trim())

      Filename = getStringValue "filename" ""
      ReportType = getStringValue "reportType" ""
      SortBy = getSortField
      SortDirection = getSortDirection
      Page = getIntValue "page" 1
      PageSize = getIntValue "pageSize" 20 }

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
let handleLoadPagedNews (ctx: HttpContext) =
    async {
        try
            let p = parseQuery ctx.Request.Query
            let c =
                DetailsQuery.newCriteria ()
                |> DetailsQuery.withTickers p.Codes
                |> DetailsQuery.withDateRange (fst p.Dates) (snd p.Dates)
                |> DetailsQuery.withFilename p.Filename
                |> DetailsQuery.withReportType p.ReportType
                |> DetailsQuery.withPagination p.Page p.PageSize
                |> DetailsQuery.withSortBy p.SortBy p.SortDirection

            match JsonStore.loadPagedNewsWithCriteria "reports.json" c with
            | Ok(r) ->
                let response =
                    {| news = r.Items
                       totalPages = r.TotalPages |}
                do! writeJsonResponse ctx HttpStatusCode.OK response
            | Error err -> do! writeErrorResponse ctx HttpStatusCode.BadRequest err
        with ex ->
            do! writeErrorResponse ctx HttpStatusCode.InternalServerError ex.Message
    }

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
                            ctx.Response.StatusCode <- 200
                            ctx.Response.ContentType <- "application/pdf"
                            let pdfBytes = System.Convert.FromBase64String(base64Pdf)
                            do! ctx.Response.Body.WriteAsync(pdfBytes, 0, pdfBytes.Length)
                        else
                            ctx.Response.StatusCode <- 502
                            do! ctx.Response.WriteAsync("PDF data missing in response.")
                    | false, _ ->
                        ctx.Response.StatusCode <- 502
                        do! ctx.Response.WriteAsync("Missing 'd' property in response JSON.")
                with ex ->
                    ctx.Response.StatusCode <- 502
                    do! ctx.Response.WriteAsync($"Error parsing PDF response: {ex.Message}")
            else
                ctx.Response.StatusCode <- 502
                do! ctx.Response.WriteAsync("Failed to fetch PDF from remote server.")
    }

let handleIndexHtml (ctx: HttpContext) =
    async {
        try
            let filePath = "report_viewer.html"
            if System.IO.File.Exists(filePath) then
                ctx.Response.StatusCode <- 200
                ctx.Response.ContentType <- "text/html; charset=utf-8"
                let! html = System.IO.File.ReadAllTextAsync(filePath) |> Async.AwaitTask
                do! ctx.Response.WriteAsync(html) |> Async.AwaitTask
            else
                ctx.Response.StatusCode <- 404
                do! ctx.Response.WriteAsync("index.html not found") |> Async.AwaitTask
        with ex ->
            ctx.Response.StatusCode <- 500
            do!
                ctx.Response.WriteAsync($"Internal server error: {ex.Message}")
                |> Async.AwaitTask
    }

let startApiServer (args: string array) =
    let builder = WebApplication.CreateBuilder args
    builder.Configuration.AddJsonFile Config.configFileName |> ignore

    // Add services
    builder.Services.AddControllers() |> ignore

    let app = builder.Build()

    // Configure middleware
    if app.Environment.IsDevelopment() then
        app.UseDeveloperExceptionPage() |> ignore

    // Custom route for /pdf/{id}
    app.MapGet("/pdf/{id}", handlePdfRequest) |> ignore

    // Route your handler directly
    app.MapGet(
        "/api/news",
        fun (ctx: HttpContext) -> handleLoadPagedNews ctx |> Async.StartAsTask :> System.Threading.Tasks.Task
    )
    |> ignore

    // Map the root route to serve index.html
    app.MapGet("/", (fun (ctx: HttpContext) -> handleIndexHtml ctx |> Async.StartAsTask :> System.Threading.Tasks.Task))
    |> ignore

    app.Run()
    0
