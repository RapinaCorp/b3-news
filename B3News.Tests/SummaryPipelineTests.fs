module B3News.Tests.SummaryPipelineTests

open System
open Xunit

open Details
open SummaryPipeline

let private detail ticker dt title url =
    { id = 1
      ticker = ticker
      dateTime = dt
      filename = title
      reportType = title
      url = url }

[<Fact>]
let ``individualSystemPrompt omits no_think by default`` () =
    let prompt = individualSystemPrompt false
    Assert.DoesNotContain("/no_think", prompt)
    Assert.Contains("in Portuguese", prompt)

[<Fact>]
let ``individualSystemPrompt prepends no_think when reasoning disabled`` () =
    let prompt = individualSystemPrompt true
    Assert.StartsWith("/no_think", prompt)
    Assert.Contains("in Portuguese", prompt)

[<Fact>]
let ``consolidatedSystemPrompt prepends no_think when reasoning disabled`` () =
    let prompt = consolidatedSystemPrompt true "PETR4"
    Assert.StartsWith("/no_think", prompt)
    Assert.Contains("PETR4", prompt)
    Assert.Contains("in Portuguese", prompt)

[<Fact>]
let ``selectRecentDocuments keeps last N per ticker`` () =
    let details =
        [ detail "PETR" "2024-01-01" "A" "http://a"
          detail "PETR" "2024-03-01" "C" "http://c"
          detail "PETR" "2024-02-01" "B" "http://b"
          detail "VALE" "2024-04-01" "V" "http://v" ]
    let selected = selectRecentDocuments details [ "petr"; "vale" ] 2
    let petr = selected |> List.find (fun (t, _) -> t = "PETR") |> snd
    Assert.Equal(2, petr.Length)
    Assert.Equal("C", petr.Head.reportType)
    Assert.Equal("B", petr.[1].reportType)

[<Fact>]
let ``runWith continues when one document fails`` () =
    let details =
        [ detail "PETR" "2024-03-01" "Ok" "http://ok"
          detail "PETR" "2024-02-01" "Bad" "http://bad" ]
    let extract (url: string) _max =
        async {
            if url.Contains "bad" then
                return Error "link fora do ar"
            else
                return
                    Ok
                        { ContentExtractor.Url = url
                          ContentExtractor.Text = "texto do comunicado"
                          ContentExtractor.SourceType = "html" }
        }
    let summarize _system (content: string) _label =
        async { return Ok("resumo de " + content.Substring(0, min 10 content.Length)) }
    let result =
        runWith extract summarize 1000 2 details [ "PETR" ] 5 false
        |> Async.RunSynchronously
    Assert.Equal(1, result.Length)
    Assert.Equal(1, result.Head.Comunicados.Length)
    Assert.Equal(1, result.Head.Falhas)
    Assert.Contains("não puderam ser processados", result.Head.ResumoConsolidado)

[<Fact>]
let ``runWith empty ticker yields placeholder`` () =
    let result =
        runWith (fun _ _ -> async { return Error "unused" }) (fun _ _ _ -> async { return Ok "" }) 100 1 [] [ "ABEV" ] 5 false
        |> Async.RunSynchronously
    Assert.Equal("ABEV", result.Head.Ativo)
    Assert.Empty(result.Head.Comunicados)
    Assert.Contains("Nenhum comunicado", result.Head.ResumoConsolidado)

[<Fact>]
let ``selectDocumentsByDate keeps only matching day and drops empty tickers`` () =
    let details =
        [ detail "ITUB" "2026-09-10 10:00:00" "A" "http://a"
          detail "PETR" "2026-09-10 11:00:00" "B" "http://b"
          detail "PETR" "2026-09-11 09:00:00" "C" "http://c"
          detail "VALE" "2026-09-11 09:00:00" "V" "http://v" ]
    let selected = selectDocumentsByDate details [ "ITUB"; "PETR"; "VALE" ] "2026-09-10"
    Assert.Equal(2, selected.Length)
    Assert.DoesNotContain(selected, fun (t, _) -> t = "VALE")
    let itub = selected |> List.find (fun (t, _) -> t = "ITUB") |> snd
    Assert.Single(itub) |> ignore
    Assert.Equal("A", itub.Head.reportType)
    let petr = selected |> List.find (fun (t, _) -> t = "PETR") |> snd
    Assert.Single(petr) |> ignore
    Assert.Equal("B", petr.Head.reportType)

[<Fact>]
let ``selectDocumentsByDate returns empty when no document matches`` () =
    let details = [ detail "ITUB" "2026-09-10 10:00:00" "A" "http://a" ]
    let selected = selectDocumentsByDate details [ "ITUB" ] "2026-09-11"
    Assert.Empty(selected)

[<Fact>]
let ``runWithDate summarizes only documents from given day`` () =
    let details =
        [ detail "ITUB" "2026-09-10 10:00:00" "A" "http://a"
          detail "PETR" "2026-09-10 11:00:00" "B" "http://b"
          detail "PETR" "2026-09-11 09:00:00" "C" "http://c" ]
    let extract (url: string) _max =
        async {
            return
                Ok
                    { ContentExtractor.Url = url
                      ContentExtractor.Text = "texto do comunicado"
                      ContentExtractor.SourceType = "html" }
        }
    let summarize _system (content: string) _label =
        async { return Ok("resumo") }
    let result =
        runWithDate extract summarize 1000 2 details [ "ITUB"; "PETR" ] "2026-09-10" false
        |> Async.RunSynchronously
    Assert.Equal(2, result.Length)
    Assert.All(result, fun (a: AtivoResumo) -> Assert.Equal(1, a.Comunicados.Length))

[<Fact>]
let ``selectDocumentsByDate does not truncate by MaxDocuments`` () =
    let details =
        [ for h in 8..14 do
              detail "PETR" (sprintf "2026-09-10 %02d:00:00" h) (sprintf "Doc%d" h) (sprintf "http://doc%d" h) ]
    let selected = selectDocumentsByDate details [ "PETR" ] "2026-09-10"
    let petr = selected |> List.find (fun (t, _) -> t = "PETR") |> snd
    Assert.Equal(7, petr.Length)

[<Fact>]
let ``selectDocumentsByDate sorts same-day documents most recent first`` () =
    let details =
        [ detail "PETR" "2026-09-10 08:00:00" "Manha" "http://manha"
          detail "PETR" "2026-09-10 12:00:00" "MeioDia" "http://meiodia"
          detail "PETR" "2026-09-10 10:00:00" "ManhaTarde" "http://manhatarde" ]
    let selected = selectDocumentsByDate details [ "PETR" ] "2026-09-10"
    let petr = selected |> List.find (fun (t, _) -> t = "PETR") |> snd
    let titles = petr |> List.map (fun d -> d.reportType)
    Assert.Equal<string list>([ "MeioDia"; "ManhaTarde"; "Manha" ], titles)

[<Theory>]
[<InlineData("2026-09-10 10:00:00", "2026-09-10")>]
[<InlineData("", "")>]
[<InlineData("2026", "")>]
[<InlineData(null, "")>]
let ``datePrefix extracts day or returns empty for malformed input`` (input: string, expected: string) =
    Assert.Equal(expected, datePrefix input)
