module B3News.Tests.ApiTests

open System
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

let private withNoNvidiaKey (f: unit -> unit) =
    let prev = Environment.GetEnvironmentVariable(Config.nvidiaApiKeyEnv)
    try
        Environment.SetEnvironmentVariable(Config.nvidiaApiKeyEnv, null)
        f ()
    finally
        Environment.SetEnvironmentVariable(Config.nvidiaApiKeyEnv, prev)

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

let private runSummaryHandler (ctx: DefaultHttpContext) =
    (handleGetSummary ctx).GetAwaiter().GetResult()
    ctx.Response.Body.Seek(0L, SeekOrigin.Begin) |> ignore
    use sr = new StreamReader(ctx.Response.Body)
    ctx.Response.StatusCode, sr.ReadToEnd()

[<Fact>]
let ``handleGetSummary returns cached summary without calling NVIDIA`` () =
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
    withNoNvidiaKey (fun () ->
        withRawTempReports
            """[{"id":1,"ticker":"PETR","dateTime":"2024-01-15 10:00:00","filename":"file1.pdf","reportType":"Fato Relevante","url":"http://example.com/1"}]"""
            (fun () ->
                let status, _ = runSummaryHandler (summaryCtx "1")
                Assert.Equal(500, status)))
