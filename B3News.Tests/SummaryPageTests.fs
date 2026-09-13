module B3News.Tests.SummaryPageTests

open System
open Xunit

open SummaryPipeline
open SummaryPage

[<Fact>]
let ``render includes ticker consolidated summary and original link`` () =
    let ativos =
        [ { Ativo = "PETR"
            Comunicados =
              [ { Data = DateTime(2024, 3, 1, 10, 0, 0)
                  Titulo = "Fato Relevante"
                  Url = "https://fnet.bmfbovespa.com.br/fnet/publico/exibirDocumento?id=99"
                  Resumo = "Empresa anunciou dividendos." } ]
            ResumoConsolidado = "Dividendos em destaque."
            Falhas = 0 } ]
    let html = render (DateTime(2026, 9, 12, 1, 0, 0)) ativos
    Assert.Contains("PETR", html)
    Assert.Contains("Dividendos em destaque.", html)
    Assert.Contains("Empresa anunciou dividendos.", html)
    Assert.Contains("https://fnet.bmfbovespa.com.br/fnet/publico/exibirDocumento?id=99", html)
    Assert.Contains("2026-09-12 01:00", html)
    Assert.Contains("não é tempo real", html)

[<Fact>]
let ``render escapes html from model output`` () =
    let ativos =
        [ { Ativo = "VALE"
            Comunicados = []
            ResumoConsolidado = "<script>alert(1)</script>"
            Falhas = 2 } ]
    let html = render DateTime.Now ativos
    Assert.DoesNotContain("<script>alert(1)</script>", html)
    Assert.Contains("&lt;script&gt;", html)
    Assert.Contains("2 comunicado(s) não puderam ser processados", html)
