module B3News.Tests.JsonStoreSummaryTests

open System
open System.IO
open Xunit

open Details

let private detail id summary =
    { id = id
      ticker = "PETR"
      dateTime = "2024-01-15 10:00:00"
      filename = sprintf "file%d.pdf" id
      reportType = "Fato Relevante"
      url = sprintf "http://example.com/%d" id
      summary = summary }

let private withTempFile (contents: string option) (f: string -> unit) =
    let path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json")
    try
        match contents with
        | Some c -> File.WriteAllText(path, c)
        | None -> ()
        f path
    finally
        if File.Exists path then
            File.Delete path

[<Fact>]
let ``updateSummary updates only the target item`` () =
    let initial = [ detail 1 ""; detail 2 "" ]
    let json = System.Text.Json.JsonSerializer.Serialize(initial)
    withTempFile (Some json) (fun path ->
        match JsonStore.updateSummary path 2 "resumo do item 2" with
        | Error err -> Assert.Fail(sprintf "expected Ok, got Error: %s" err)
        | Ok () ->
            match JsonStore.tryRead path with
            | Error err -> Assert.Fail(sprintf "tryRead failed: %s" err)
            | Ok items ->
                Assert.Equal(2, items.Length)
                let untouched = items |> List.find (fun d -> d.id = 1)
                let updated = items |> List.find (fun d -> d.id = 2)
                Assert.Equal("", untouched.summary)
                Assert.Equal("resumo do item 2", updated.summary))

[<Fact>]
let ``updateSummary returns Error for missing id`` () =
    let initial = [ detail 1 "" ]
    let json = System.Text.Json.JsonSerializer.Serialize(initial)
    withTempFile (Some json) (fun path ->
        match JsonStore.updateSummary path 99 "resumo" with
        | Ok () -> Assert.Fail("expected Error for missing id")
        | Error err -> Assert.Contains("99", err))

[<Fact>]
let ``tryRead tolerates items without summary field`` () =
    let legacyJson =
        """[{"id":1,"ticker":"PETR","dateTime":"2024-01-15 10:00:00","filename":"file1.pdf","reportType":"Fato Relevante","url":"http://example.com/1"}]"""
    withTempFile (Some legacyJson) (fun path ->
        match JsonStore.tryRead path with
        | Error err -> Assert.Fail(sprintf "tryRead failed: %s" err)
        | Ok items ->
            Assert.Single(items) |> ignore
            Assert.True(
                String.IsNullOrEmpty(items.Head.summary),
                sprintf "expected null/empty summary, got '%s'" (string (box items.Head.summary)))
            // And it can be summarized afterwards
            match JsonStore.updateSummary path 1 "novo resumo" with
            | Error err -> Assert.Fail(sprintf "updateSummary failed: %s" err)
            | Ok () ->
                match JsonStore.tryRead path with
                | Error err -> Assert.Fail(sprintf "re-read failed: %s" err)
                | Ok reread -> Assert.Equal("novo resumo", (reread |> List.head).summary))

[<Fact>]
let ``updateSummary writes atomically and leaves no temp file`` () =
    let initial = [ detail 1 "" ]
    let json = System.Text.Json.JsonSerializer.Serialize(initial)
    withTempFile (Some json) (fun path ->
        // Simulate a leftover temp from a previous crash mid-write: the next
        // write must overwrite and consume it, not fail or keep it around.
        File.WriteAllText(path + ".tmp", "garbage de um crash anterior")
        match JsonStore.updateSummary path 1 "resumo" with
        | Error err -> Assert.Fail(sprintf "updateSummary failed: %s" err)
        | Ok () ->
            Assert.False(File.Exists(path + ".tmp"), "temp file must be consumed by the atomic write")
            match JsonStore.tryRead path with
            | Error err -> Assert.Fail(sprintf "tryRead failed: %s" err)
            | Ok items ->
                Assert.Single(items) |> ignore
                Assert.Equal("resumo", items.Head.summary))

[<Fact>]
let ``mergeDetails tolerates duplicate keys in existing`` () =
    // Regression: Map.ofList used to throw ArgumentException when legacy
    // reports.json carried two records with the same
    // (ticker, dateTime, filename, url) key.
    let d = detail 1 "resumo antigo"
    let existing = [ d; { d with summary = "" } ]

    let added, merged = JsonStore.mergeDetails existing []

    Assert.Equal(0, added)
    Assert.Equal(1, List.length merged) // duplicates collapse; first occurrence wins
    Assert.Equal("resumo antigo", (merged |> List.head).summary)

[<Fact>]
let ``mergeDetails preserves cached summary and counts only new keys`` () =
    let existing = [ detail 1 "resumo cacheado" ]
    let refetched = detail 1 "" // same key as existing, fresh fetch, no summary yet
    let fresh = detail 2 ""

    let added, merged = JsonStore.mergeDetails existing [ refetched; fresh ]

    Assert.Equal(1, added)
    Assert.Equal(2, List.length merged)
    let one = merged |> List.find (fun x -> x.id = 1)
    Assert.Equal("resumo cacheado", one.summary)

[<Fact>]
let ``mergeDetails counts duplicate fetched keys once`` () =
    let existing = [ detail 1 "" ]
    let dupA = detail 2 ""
    let dupB = { detail 2 "" with summary = "fetch duplicado" } // same key as dupA

    let added, merged = JsonStore.mergeDetails existing [ dupA; dupB ]

    Assert.Equal(1, added)
    Assert.Equal(2, List.length merged)
