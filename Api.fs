module Api

open System.Text.Json
open Microsoft.AspNetCore.Http
open System.Net
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting

type QueryParams =
    { Codes: string list
      Date: string
      Filename: string
      ReportType: string
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

    { Codes =
        getStringValue "codes" ""
        |> fun s -> if s = "" then [] else s.Split(',') |> Array.toList
      Date = getStringValue "date" ""
      Filename = getStringValue "filename" ""
      ReportType = getStringValue "reportType" ""
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
                |> DetailsQuery.withDateRange p.Date p.Date
                |> DetailsQuery.withFilename p.Filename
                |> DetailsQuery.withReportType p.ReportType
                |> DetailsQuery.withPagination p.Page p.PageSize

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

let startApiServer (args: string array) =
    let builder = WebApplication.CreateBuilder args

    // Add services
    builder.Services.AddControllers() |> ignore

    let app = builder.Build()

    // Configure middleware
    if app.Environment.IsDevelopment() then
        app.UseDeveloperExceptionPage() |> ignore

    // Route your handler directly
    app.MapGet(
        "/api/news",
        fun (ctx: HttpContext) -> handleLoadPagedNews ctx |> Async.StartAsTask :> System.Threading.Tasks.Task
    )
    |> ignore

    app.Run()
    0
