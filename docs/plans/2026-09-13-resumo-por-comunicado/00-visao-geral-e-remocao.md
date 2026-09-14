# Passo 0 — Visão geral da mudança e remoção da feature antiga

## O que muda

A feature de resumos do b3-news vai ser refeita. Em vez de gerar uma página
HTML separada com resumo consolidado por ativo (comando `summarize`, rota
`/resumos`), o resumo passa a ser **por comunicado individual**, armazenado
como um novo campo `summary` diretamente em cada item de `reports.json`.

Na UI (`report_viewer.html` e `report_viewer_legacy.html`), a coluna "Tipo de
Relatório" ganha um ícone de chevron (▾) ao final do texto/link. Ao clicar,
abre um popup/modal mostrando o resumo daquele comunicado específico — gerado
sob demanda na primeira vez que alguém pede (chamando a NVIDIA), e cacheado
no próprio `reports.json` a partir daí (o texto de um comunicado já publicado
não muda, então o resumo nunca fica desatualizado — não há necessidade de
TTL, refresh manual ou tratamento de "incompleto").

## Por que remover a feature antiga

O resumo consolidado por ativo e a geração em lote por data (`summarize`,
`SummaryPage.fs`) foram uma primeira tentativa, mas a abordagem por
comunicado individual é mais simples, reaproveita a listagem que já existe,
e elimina o problema de resumo "incompleto" no meio do dia. Foi decidido
remover a feature antiga por completo, não deixar as duas coexistindo.

## Itens a remover

1. **`SummaryPage.fs`** — apagar o arquivo inteiro (só existia para renderizar
   a página HTML de resumo consolidado).
2. **`SummaryPipeline.fs`** — apagar o arquivo inteiro. A lógica de extrair +
   resumir **um único documento** será recriada, mais enxuta, no passo 1
   (`NewsSummary.fs`) — não tente reaproveitar este arquivo por partes,
   é mais simples recomeçar do zero para o caso de uso único.
3. **`Program.fs`**:
   - Remover a função `summarize` inteira.
   - Remover a linha de `usage()` que documenta o comando `summarize`.
   - Remover o `case` `"summarize" -> summarize args[1..]` do `match` em `main`.
   - Manter `isDateFormat` (ainda usado por `update-json`).
4. **`Api.fs`**:
   - Remover a rota `app.MapGet("/resumos", handleHtmlFile (...))`.
5. **`Config.fs`**:
   - Simplificar `SummarySettings` para conter só o que o resumo por
     comunicado precisa: `Model`, `MaxChars`, `MaxRetries`, `DisableReasoning`.
   - Remover `MaxDocuments`, `MaxParallelism` e `OutputFile` do tipo, do
     `defaultSummarySettings` e de `mergeSummary` (não são mais usados: cada
     resumo agora é gerado um de cada vez, por requisição HTTP, não em lote
     paralelo).
6. **`B3News.fsproj`**:
   - Remover `<Compile Include="SummaryPipeline.fs" />` e
     `<Compile Include="SummaryPage.fs" />`.
   - Adicionar `<Compile Include="NewsSummary.fs" />` no lugar (ver passo 1),
     logo após `ContentExtractor.fs` e antes de `Api.fs`.
7. **`b3news.settings.json`**: revisar o bloco `"Summary"` e remover chaves
   que não existem mais no tipo `SummarySettings` (`MaxDocuments`,
   `OutputFile`, se estiverem presentes — hoje não estão, então talvez não
   haja nada a mudar aqui; conferir mesmo assim).

## Não remover

- `NvidiaClient.fs` e `ContentExtractor.fs` — continuam sendo os blocos de
  construção de baixo nível (chamada HTTP à NVIDIA e extração de
  texto de HTML/PDF), só passam a ser chamados por um novo módulo menor.

## Ordem dos próximos passos

1. `01-schema-e-persistencia.md` — campo `summary` em `NewsDetail` + função de
   atualização segura em `JsonStore.fs` + novo módulo `NewsSummary.fs`.
2. `02-endpoint-api.md` — endpoint HTTP que busca ou gera o resumo sob demanda.
3. `03-frontend-report-viewer.md` — chevron + popup no `report_viewer.html`.
4. `04-frontend-legacy.md` — o mesmo no `report_viewer_legacy.html`.

Compile o projeto (`dotnet build`) ao final deste passo para confirmar que a
remoção não deixou nada quebrado antes de seguir para o passo 1.
