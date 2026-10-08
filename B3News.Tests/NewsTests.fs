module B3News.Tests.NewsTests

open Xunit
open System
open News

/// Shape copied from a real `news_cache` list file written by `fetchAllNews`
/// (B3's `ListarTitulosNoticias` payload): `ticker` absent, `content` extra.
let sampleJson =
    """[{"NwsMsg":{"IdAgencia":18,"content":null,"dateTime":"2026-09-15 19:58:20","headline":"ETF BV GDIV (GDIV) 63.209.544/0001-88 - Balancete - 08/2026","id":3484642}}]"""

[<Fact>]
let ``tryParseCache decodes a valid cached list`` () =
    match tryParseCache sampleJson with
    | Ok xs ->
        Assert.Equal(1, List.length xs)
        let msg = xs.Head.NwsMsg
        Assert.Equal(18, msg.IdAgencia)
        Assert.Equal(3484642, msg.id)
        Assert.Equal(None, msg.ticker)
        Assert.Equal("2026-09-15 19:58:20", msg.dateTime)
        Assert.Equal("ETF BV GDIV (GDIV) 63.209.544/0001-88 - Balancete - 08/2026", msg.headline)
    | Error err ->
        Assert.True(false, sprintf "expected Ok, got error: %s" err)

[<Fact>]
let ``tryParseCache returns Error for corrupted cache`` () =
    match tryParseCache "<html>not json</html>" with
    | Error _ -> ()
    | Ok _ -> Assert.True(false, "expected Error for non-JSON cache")

[<Fact>]
let ``tryParseCache returns Error for truncated JSON`` () =
    let truncated = """[{"NwsMsg":{"IdAgencia":18,"""
    match tryParseCache truncated with
    | Error _ -> ()
    | Ok _ -> Assert.True(false, "expected Error for truncated JSON")

[<Fact>]
let ``tryParseCache returns Error for empty string`` () =
    match tryParseCache "" with
    | Error _ -> ()
    | Ok _ -> Assert.True(false, "expected Error for empty cache")

[<Fact>]
let ``isCacheable accepts only ranges ending before today`` () =
    let today = DateTime(2026, 10, 8)
    Assert.True(isCacheable today (DateTime(2026, 10, 7, 23, 59, 59)))
    Assert.True(isCacheable today (DateTime(2025, 1, 1)))
    Assert.False(isCacheable today today)
    Assert.False(isCacheable today (DateTime(2026, 10, 8, 14, 30, 0)))
    Assert.False(isCacheable today (DateTime(2026, 10, 9)))

[<Fact>]
let ``todayB3 is a date at most one day from UTC today`` () =
    let t = todayB3 ()
    let delta = abs ((t - DateTime.UtcNow.Date).TotalDays)
    Assert.True(delta <= 1.0, sprintf "todayB3=%A too far from UTC today" t)
    Assert.Equal(t, t.Date)
