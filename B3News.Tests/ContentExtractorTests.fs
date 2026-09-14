module B3News.Tests.ContentExtractorTests

open System
open System.Text
open Xunit

open Download
open ContentExtractor

[<Fact>]
let ``extractHtml strips scripts and keeps paragraph text`` () =
    let html =
        """<html><head><script>alert(1)</script></head>
           <body><nav>menu</nav><p>Fato relevante sobre dividendos</p><footer>rodapé</footer></body></html>"""
    let bytes = Encoding.UTF8.GetBytes html
    match extractHtml bytes 8000 with
    | Error err -> failwith err
    | Ok text ->
        Assert.Contains("Fato relevante sobre dividendos", text)
        Assert.DoesNotContain("alert", text)
        Assert.DoesNotContain("menu", text)
        Assert.DoesNotContain("rodapé", text)

[<Fact>]
let ``extractHtml truncates to maxChars`` () =
    let html = "<html><body>" + String.replicate 50 "palavra " + "</body></html>"
    let bytes = Encoding.UTF8.GetBytes html
    match extractHtml bytes 20 with
    | Ok text -> Assert.True(text.Length <= 20)
    | Error err -> failwith err

[<Fact>]
let ``extractFromBytes html returns source type`` () =
    let bytes = Encoding.UTF8.GetBytes "<html><body><p>Oi</p></body></html>"
    match extractFromBytes "http://example/a" Html bytes 8000 with
    | Ok c ->
        Assert.Equal("html", c.SourceType)
        Assert.Contains("Oi", c.Text)
    | Error err -> failwith err

[<Fact>]
let ``extractPdf reads text from a minimal pdf`` () =
    let pdf =
        "%PDF-1.1\n" +
        "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n" +
        "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n" +
        "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>\nendobj\n" +
        "4 0 obj\n<< /Length 44 >>\nstream\nBT /F1 24 Tf 100 700 Td (Hello PDF) Tj ET\nendstream\nendobj\n" +
        "5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n" +
        "xref\n0 6\n0000000000 65535 f \n0000000009 00000 n \n0000000058 00000 n \n0000000115 00000 n \n0000000266 00000 n \n0000000360 00000 n \n" +
        "trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n429\n%%EOF\n"
    let bytes = Encoding.ASCII.GetBytes pdf
    match extractPdf bytes 8000 with
    | Ok text -> Assert.Contains("Hello PDF", text)
    | Error err -> failwith err
