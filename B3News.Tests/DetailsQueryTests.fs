module B3News.Tests.DetailsQueryTests

open System
open Xunit
open Details
open DetailsQuery

let createSampleNews () =
    [ { id = 1
        ticker = "PETR4"
        dateTime = "2024-01-15 10:00:00"
        reportType = "Fato Relevante"
        filename = "petr4_fr_20240115.pdf"
        url = "http://example.com/1"
        summary = "" }
      { id = 2
        ticker = "VALE3"
        dateTime = "2024-01-16 11:00:00"
        reportType = "Relatório Trimestral"
        filename = "vale3_itr_20240116.pdf"
        url = "http://example.com/2"
        summary = "" }
      { id = 3
        ticker = "ITUB4"
        dateTime = "2024-01-17 12:00:00"
        reportType = "Aviso aos Acionistas"
        filename = "itub4_av_20240117.pdf"
        url = "http://example.com/3"
        summary = "" }
      { id = 4
        ticker = "PETR4"
        dateTime = "2024-01-18 13:00:00"
        reportType = "Fato Relevante"
        filename = "petr4_fr_20240118.pdf"
        url = "http://example.com/4"
        summary = "" } ]

[<Fact>]
let ``Test filterByCodes`` () =
    let news = createSampleNews ()
    let filtered = news |> List.filter (filterByCodes [ "PETR4" ])
    Assert.Equal(2, List.length filtered)
    Assert.True(filtered |> List.forall (fun n -> n.ticker = "PETR4"))

[<Fact>]
let ``Test filterByDate`` () =
    let news = createSampleNews ()
    let filtered = news |> List.filter (filterByDate "2024-01-16" "2024-01-17")
    Assert.Equal(2, List.length filtered)
    let tickers = filtered |> List.map (fun n -> n.ticker) |> set
    Assert.Contains("VALE3", tickers)
    Assert.Contains("ITUB4", tickers)

[<Fact>]
let ``Test filterByFilename`` () =
    let news = createSampleNews ()
    let filtered = news |> List.filter (filterByFilename "petr4")
    Assert.Equal(2, List.length filtered)
    Assert.True(filtered |> List.forall (fun n -> n.filename.Contains "petr4"))

[<Fact>]
let ``Test filterByReportType`` () =
    let news = createSampleNews ()
    let filtered = news |> List.filter (filterByReportType "Fato Relevante")
    Assert.Equal(2, List.length filtered)
    Assert.True(filtered |> List.forall (fun n -> n.reportType = "Fato Relevante"))

[<Fact>]
let ``Test paginate`` () =
    let news = createSampleNews ()
    let paged = paginate 2 1 news
    Assert.Equal(2, List.length paged.Items)
    Assert.Equal(2, paged.TotalPages)
    Assert.Equal(4, paged.TotalRecords)
    Assert.Equal("PETR4", paged.Items.[0].ticker)

[<Fact>]
let ``Test sortNews ByDate Asc`` () =
    let news = createSampleNews ()
    let sorted = sortNews ByDate Asc news
    Assert.Equal("PETR4", sorted.[0].ticker)
    Assert.Equal("2024-01-15 10:00:00", sorted.[0].dateTime)

[<Fact>]
let ``Test sortNews ByDate Desc`` () =
    let news = createSampleNews ()
    let sorted = sortNews ByDate Desc news
    Assert.Equal("PETR4", sorted.[0].ticker)
    Assert.Equal("2024-01-18 13:00:00", sorted.[0].dateTime)

[<Fact>]
let ``Test filterAll with multiple criteria`` () =
    let news = createSampleNews ()
    let criteria =
        newCriteria ()
        |> withTickers [ "PETR4" ]
        |> withDateRange "2024-01-01" "2024-01-31"
        |> withReportType "Fato Relevante"
        |> withSortBy ByDate Desc
    let result = filterAll criteria news
    Assert.Equal(2, result.TotalRecords)
    Assert.Equal("2024-01-18 13:00:00", result.Items.[0].dateTime)

[<Theory>]
[<InlineData(null, null, 1, 1)>]
[<InlineData("PETR4", null, 1, 1)>]
[<InlineData("PETR4", null, 3, 2)>]
[<InlineData(null, "Fato Relevante", 1, 1)>]
[<InlineData("VALE3", "Relatório Trimestral", 1, 1)>]
[<InlineData("VALE3", "Relatório Trimestral", 2, 1)>]
let ``Test filterAll with ClientRecordCount and other filters``
    (
        ticker: string,
        reportType: string,
        clientRecordCount: int,
        expectedCount: int
    ) =
    let news = createSampleNews ()
    let criteria =
        newCriteria ()
        |> (fun c -> if ticker <> null then withTickers [ ticker ] c else c)
        |> (fun c ->
            if reportType <> null then
                withReportType reportType c
            else
                c)
        |> withClientRecordCount (Some clientRecordCount)

    let result = filterAll criteria news
    Assert.Equal(expectedCount, result.TotalRecords)
    Assert.Equal(expectedCount, List.length result.Items)
