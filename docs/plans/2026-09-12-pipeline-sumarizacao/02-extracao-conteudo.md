# Passo 2 — Extração de texto de HTML e PDF

## Objetivo

Criar um módulo (ex: `ContentExtractor.fs`) que, dado um link de comunicado
(URL), baixe o conteúdo e retorne o texto principal em formato plano,
independentemente de o link apontar para uma página HTML ou um PDF.

## Requisitos

- Detectar o tipo de conteúdo pelo `Content-Type` da resposta HTTP (ou pela
  extensão do link como fallback), não assumir pelo domínio.
- **PDF**: extrair o texto usando uma lib .NET de extração de PDF (ex:
  `PdfPig` ou equivalente NuGet já compatível com F#/.NET usado no projeto).
  Escolher a lib de menor complexidade de dependências que resolva o caso.
- **HTML**: extrair apenas o texto principal do conteúdo (remover
  navegação, rodapé, scripts, tags) — não é necessário um extrator de
  "reading mode" sofisticado, um parser HTML (ex: `HtmlAgilityPack` ou
  equivalente) que pegue o texto de blocos de conteúdo (`<p>`, `<div>`
  principal) já resolve.
- Truncar o texto extraído a um limite configurável de caracteres (padrão:
  6000-8000 caracteres) antes de retornar, para controlar o tamanho enviado
  ao LLM no passo 3.
- Se a extração falhar (PDF corrompido, página inacessível, 404, etc), retornar
  um erro claro em vez de lançar exceção não tratada — o pipeline deve
  conseguir pular esse comunicado e seguir com os demais.

## Assinatura sugerida

```fsharp
type ExtractedContent = { Url: string; Text: string; SourceType: string }

val extractText : url: string -> maxChars: int -> Async<Result<ExtractedContent, string>>
```

## Reaproveitamento

Verificar se o `Download.fs` já tem lógica de download HTTP reutilizável
(client, headers, tratamento de timeout) e reaproveitar em vez de duplicar.

## Critério de aceite

- Testar com pelo menos um link real de PDF e um link real de página HTML
  (usar exemplos reais do Fnet/Enet) e confirmar que o texto extraído é
  legível e não contém lixo de marcação/navegação.
- Falha em um link não impede o processamento dos demais.
