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
let ``detectKind finds html marker inside the 64KB sniff window`` () =
    let filler = Array.create (64 * 1024 - 100) 32uy // spaces
    let marker = Encoding.ASCII.GetBytes("<HTML>")
    let bytes = Array.append filler marker
    Assert.Equal(Html, Download.detectKind bytes)

[<Fact>]
let ``detectKind misses html marker beyond sniff window and defaults to Pdf`` () =
    // Bounded sniff (embedded decision, see docs/plans/2026-09-17-review-fixes):
    // a marker past the window classifies as the Pdf default, same as
    // content without any known magic.
    let filler = Array.create (64 * 1024 + 8) 32uy
    let marker = Encoding.ASCII.GetBytes("<HTML>")
    let bytes = Array.append filler marker
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

[<Fact>]
let ``classify recognizes fnet exibirDocumento url`` () =
    match Download.classify "https://fnet.bmfbovespa.com.br/fnet/publico/exibirDocumento?id=123" with
    | Fnet id -> Assert.Equal(123, id)
    | _ -> failwith "Expected Fnet"

[<Fact>]
let ``classify recognizes local pdf proxy url`` () =
    match Download.classify "/pdf/456.pdf" with
    | Enet id -> Assert.Equal(456, id)
    | _ -> failwith "Expected Enet"

[<Fact>]
let ``classify recognizes enetWEB url`` () =
    let result =
        Download.classify "https://www.rad.cvm.gov.br/ENETWEB/frmExibirArquivoIPEExterno.aspx?ID=456"
    match result with
    | Enet id -> Assert.Equal(456, id)
    | _ -> failwith "Expected Enet"

[<Fact>]
let ``describeFetchErr renders Rejected`` () =
    let err = Download.Rejected "motivo qualquer"
    let desc = Download.describeFetchErr err
    Assert.Equal("CVM rejeitou a requisição: motivo qualquer", desc)

[<Fact>]
let ``describeFetchErr renders CaptchaRequired`` () =
    let err = Download.CaptchaRequired "123"
    let desc = Download.describeFetchErr err
    Assert.Equal("CVM exigiu captcha para o protocolo 123 (requer navegador)", desc)

[<Fact>]
let ``httpStatusForFetchErr maps Rejected to 502`` () =
    let err = Download.Rejected "qualquer motivo"
    let status = Download.httpStatusForFetchErr err
    Assert.Equal(502, status)

[<Fact>]
let ``httpStatusForFetchErr maps CaptchaRequired to 503`` () =
    let err = Download.CaptchaRequired "123"
    let status = Download.httpStatusForFetchErr err
    Assert.Equal(503, status)
