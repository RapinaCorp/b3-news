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
