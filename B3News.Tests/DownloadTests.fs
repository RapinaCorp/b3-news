module B3News.Tests.DownloadTests

open System
open System.IO
open System.Text
open Xunit

open Download

[<Fact>]
let ``classify recognizes fnet url`` () =
    let result = Download.classify "https://fnet.bmfbovespa.com.br/fnet/publico/visualizarDocumento?id=123"
    match result with
    | Fnet id -> Assert.Equal(123, id)
    | _ -> failwith "Expected Fnet"

[<Fact>]
let ``classify recognizes enet url`` () =
    let result =
        Download.classify "https://www.rad.cvm.gov.br/ENET/frmExibirArquivoIPEExterno.aspx?ID=456"
    match result with
    | Enet id -> Assert.Equal(456, id)
    | _ -> failwith "Expected Enet"

[<Fact>]
let ``classify recognizes enet nbc url`` () =
    let result = Download.classify "https://www.rad.cvm.gov.br/ENET/frmNBC.aspx?NSD=789"
    match result with
    | EnetNbc nsd -> Assert.Equal("https://www.rad.cvm.gov.br/ENET/frmNBC.aspx?NSD=789", nsd)
    | _ -> failwith "Expected EnetNbc"

[<Fact>]
let ``classify falls back to unknown`` () =
    let url = "https://example.com/anything"
    match Download.classify url with
    | Unknown u -> Assert.Equal(url, u)
    | _ -> failwith "Expected Unknown"

[<Fact>]
let ``classify ignores malformed id`` () =
    match Download.classify "https://fnet.bmfbovespa.com.br/fnet/publico/visualizarDocumento?id=abc" with
    | Unknown _ -> ()
    | _ -> failwith "Expected Unknown for non-numeric id"

[<Fact>]
let ``detectKind returns Pdf for pdf magic`` () =
    let bytes = Encoding.UTF8.GetBytes("%PDF-1.4 some pdf content")
    Assert.Equal(Pdf, Download.detectKind bytes)

[<Fact>]
let ``detectKind returns Html for html marker`` () =
    let bytes = Encoding.UTF8.GetBytes("<html><body>not a pdf</body></html>")
    Assert.Equal(Html, Download.detectKind bytes)

[<Fact>]
let ``detectKind defaults to Pdf for unknown content`` () =
    let bytes = Encoding.UTF8.GetBytes("just some plain text")
    Assert.Equal(Pdf, Download.detectKind bytes)

[<Fact>]
let ``detectKind matches pdf case-insensitively`` () =
    let bytes = Encoding.UTF8.GetBytes("%pdf-1.4")
    Assert.Equal(Pdf, Download.detectKind bytes)

[<Fact>]
let ``getHtmlLink routes fnet to exibirDocumento`` () =
    let link = Download.getHtmlLink "https://fnet.bmfbovespa.com.br/fnet/publico/visualizarDocumento?id=123" "text"
    Assert.Equal(
        "<a href='https://fnet.bmfbovespa.com.br/fnet/publico/exibirDocumento?id=123' target='_self'>text</a>",
        link
    )

[<Fact>]
let ``getHtmlLink routes enet to local pdf proxy`` () =
    let link = Download.getHtmlLink "https://www.rad.cvm.gov.br/ENET/frmExibirArquivoIPEExterno.aspx?ID=456" "text"
    Assert.Equal("<a href='/pdf/456.pdf' target='_self'>text</a>", link)

[<Fact>]
let ``getRealUrl routes fnet to exibirDocumento`` () =
    Assert.Equal(
        "https://fnet.bmfbovespa.com.br/fnet/publico/exibirDocumento?id=123",
        Download.getRealUrl "https://fnet.bmfbovespa.com.br/fnet/publico/visualizarDocumento?id=123"
    )

[<Fact>]
let ``getRealUrl routes enet to local pdf proxy`` () =
    Assert.Equal(
        "/pdf/456.pdf",
        Download.getRealUrl "https://www.rad.cvm.gov.br/ENET/frmExibirArquivoIPEExterno.aspx?ID=456"
    )

[<Fact>]
let ``getRealUrl passes through nbc url`` () =
    let nbcUrl = "https://www.rad.cvm.gov.br/ENET/frmNBC.aspx?NSD=789"
    Assert.Equal(nbcUrl, Download.getRealUrl nbcUrl)

[<Fact>]
let ``getRealUrl passes through unknown url`` () =
    let url = "https://example.com/anything"
    Assert.Equal(url, Download.getRealUrl url)

let private uniqueFolder () =
    "_tests_" + Guid.NewGuid().ToString("N")

[<Fact>]
let ``save writes new file with pdf extension`` () =
    let folder = uniqueFolder ()
    try
        let bytes = Encoding.UTF8.GetBytes("%PDF-1.4")
        let result = Download.save folder "doc" bytes Pdf |> Async.RunSynchronously
        match result with
        | Ok (Written path) ->
            Assert.EndsWith("doc.pdf", path)
            Assert.True(File.Exists path)
        | Ok (Skipped _) -> failwith "Expected Written"
        | Error err -> failwith (sprintf "Expected success, got %A" err)
    finally
        if Directory.Exists(Path.Combine("report", folder)) then
            Directory.Delete(Path.Combine("report", folder), true)

[<Fact>]
let ``save writes html extension for html kind`` () =
    let folder = uniqueFolder ()
    try
        let bytes = Encoding.UTF8.GetBytes("<html></html>")
        let result = Download.save folder "doc" bytes Html |> Async.RunSynchronously
        match result with
        | Ok (Written path) ->
            Assert.EndsWith("doc.html", path)
            Assert.True(File.Exists path)
        | Ok (Skipped _) -> failwith "Expected Written"
        | Error err -> failwith (sprintf "Expected success, got %A" err)
    finally
        if Directory.Exists(Path.Combine("report", folder)) then
            Directory.Delete(Path.Combine("report", folder), true)

[<Fact>]
let ``save skips existing file`` () =
    let folder = uniqueFolder ()
    try
        let path = Path.Combine("report", folder, "doc.pdf")
        Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
        File.WriteAllBytes(path, Array.empty)
        let result = Download.save folder "doc" (Encoding.UTF8.GetBytes("%PDF-1.4")) Pdf |> Async.RunSynchronously
        match result with
        | Ok (Skipped skippedPath) -> Assert.Equal(path, skippedPath)
        | Ok (Written _) -> failwith "Expected Skipped"
        | Error err -> failwith (sprintf "Expected Skipped, got %A" err)
    finally
        if Directory.Exists(Path.Combine("report", folder)) then
            Directory.Delete(Path.Combine("report", folder), true)
