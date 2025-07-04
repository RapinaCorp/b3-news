module B3News.Tests.ApiTests

open System
open System.Collections.Generic
open Microsoft.AspNetCore.Http
open Moq
open Xunit
open Api

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
