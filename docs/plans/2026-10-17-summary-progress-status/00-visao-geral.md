# Passo 0 — Visão geral, contrato HTTP e decisões

## O que muda

Hoje `GET /api/news/{id}/summary` bloqueia até o pipeline inteiro terminar
(`ContentExtractor.extractText` + `OpenAIClient.summarize`), então os viewers
só conseguem mostrar um "Gerando resumo…" estático. A mudança faz o endpoint
responder na hora com a etapa corrente, e os dois viewers passam a fazer
polling até receber o resumo final (ou um erro):

1. Viewer faz GET.
2. Resposta é `{summary}` (já cacheado) ou `{status, message}` (job rodando).
3. Viewer continua o polling enquanto vê `status`; `summary` ou erro encerram.

Em escopo: `Api.fs`, `NewsSummary.fs`, `B3News.Tests/ApiTests.fs`,
`report_viewer.html`, `report_viewer_legacy.html`, `AGENTS.md`, `CONTEXT.md`.
Fora de escopo: SSE/websocket, persistir estado do job, limite global de
concorrência, harness de teste JS.

Design completo (com análise de memória e resolução dos achados da revisão de
arquitetura): `docs/specs/2026-10-17-summary-progress-design.md`.

## Contrato HTTP (`GET /api/news/{id}/summary`)

| Situação | Status | Corpo |
|---|---|---|
| `id` inválido | 400 | `{error}` (inalterado) |
| `id` desconhecido | 404 | `{error}` (inalterado) |
| Falha de leitura do store (handler) | 500 | `{error}` (inalterado) |
| Resumo cacheado no `reports.json` | 200 | `{summary}` (inalterado) |
| Sem chave LLM e sem job rodando | 500 | `{error}`, **sem iniciar job** (preserva teste atual) |
| Sem cache, sem job → job iniciado por esta requisição | 200 | `{status:"fetching", message:"Buscando o conteúdo do comunicado…"}` |
| Job rodando — extração | 200 | `{status:"fetching", message:"Buscando o conteúdo do comunicado…"}` |
| Job rodando — LLM | 200 | `{status:"summarizing", message:"Gerando o resumo com IA…"}` |
| Job terminou, persist OK | 200 | `{summary}` (cache do store; entrada já removida) |
| Job terminou, persist FALHOU | 200 | `{summary}` (servido **uma vez** da memória, removido na leitura) |
| Job falhou (geração) | 502 | `{error}` (resposta calculada do estado, depois removido) |
| Job falhou (leitura do store) | 500 | `{error}` (mesma remoção) |

O servidor é dono do token `status` (máquina) e da `message` (texto PT).

## Decisões de implementação (fechadas)

1. **`TryAdd` é o único gate de job.** `summaryGates` e `getOrGenerateSummary`
   são deletados.
2. **Um escritor por entrada.** Só o pipeline dono escreve `Running` e o
   estado terminal; leitores nunca removem `Running`.
3. **Respostas terminais são idempotentes.** O handler calcula a resposta a
   partir do estado lido e faz `TryRemove` em melhor esforço — dois polls
   concorrentes recebem a mesma resposta terminal e o perdedor **não** reinicia
   a geração.
4. **Persist antes da transição terminal.** `Ok` → remove entrada;
   `Error` → deixa `Succeeded summary`.
5. **Pipeline relê o `reports.json` uma vez depois de assumir o job**, antes de
   gerar (preserva a garantia do "re-read dentro do gate" do código atual).
6. **`try/with` total no pipeline**, incluindo chamadas de `onStep` — qualquer
   surpresa vira `Failed(GenerationError …)` via `Log.error`, nunca um
   `Running` preso.
7. **Registry é consultado antes do `tryRead`** — polls de 2 s não releem o
   `reports.json` com job rodando.
8. **Re-despacho limitado quando perde o `TryAdd`**: re-consulta até 3 vezes
   (normalmente o estado do vencedor aparece na primeira); depois 500.

## Ordem dos passos

1. `01-backend-core.md` — `SummaryStep`/`onStep` em `NewsSummary.fs` + máquina
   de estados em `Api.fs`.
2. `02-handler-endpoint.md` — `handleGetSummaryWith` + composição real.
3. `03-testes-backend.md` — reescreve a suíte do resumo em `ApiTests.fs`.
4. `04-frontend-report-viewer.md` — polling no viewer moderno.
5. `05-frontend-legacy.md` — polling ES5 no viewer legacy.
6. `06-docs-e-verificacao.md` — `AGENTS.md`, `CONTEXT.md`, verificação manual.

## Critério de aceite (geral)

- Primeiro GET em item sem resumo responde 200 com `status` em vez de bloquear.
- Polls subsequentes mostram `fetching` → `summarizing` → `{summary}`.
- Item já cacheado responde `{summary}` direto, sem job e sem I/O extra de
  registry.
- Falha de geração responde 502 na leitura seguinte e o retry (novo GET)
  reinicia a geração.
- Falha de persist expõe o resumo gerado uma vez e depois reinicia.
- `dotnet build` e `dotnet test` verdes; smoke manual nos dois viewers.
