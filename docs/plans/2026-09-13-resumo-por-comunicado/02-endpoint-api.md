# Passo 2 — Endpoint HTTP: buscar ou gerar o resumo sob demanda

## Objetivo

Adicionar em `Api.fs` um endpoint `GET /api/news/{id}/summary` que:

1. Lê `reports.json` e localiza o item pelo `id` da rota.
2. Se o item já tem `summary` não-vazio, retorna direto (sem chamar a NVIDIA).
3. Se não tem, gera na hora (via `NewsSummary.summarizeDetail`), salva com
   `JsonStore.updateSummary`, e então retorna o resumo gerado.
4. Se a geração falhar, retorna um erro claro — **sem** persistir nada, para
   que o usuário possa simplesmente clicar de novo e tentar outra vez.

## Implementação sugerida

```fsharp
let handleGetSummary (ctx: HttpContext) : Task =
    task {
        let idStr = ctx.Request.RouteValues["id"] :?> string
        match System.Int32.TryParse idStr with
        | false, _ ->
            do! writeErrorResponse ctx HttpStatusCode.BadRequest "ID inválido"
        | true, id ->
            match JsonStore.tryRead Config.reportsJson with
            | Error err ->
                do! writeErrorResponse ctx HttpStatusCode.InternalServerError err
            | Ok details ->
                match details |> List.tryFind (fun d -> d.id = id) with
                | None ->
                    do! writeErrorResponse ctx HttpStatusCode.NotFound "Comunicado não encontrado"
                | Some detail when not (System.String.IsNullOrWhiteSpace detail.summary) ->
                    do! writeJsonResponse ctx HttpStatusCode.OK {| summary = detail.summary |}
                | Some detail ->
                    match Config.nvidiaApiKey () with
                    | Error err ->
                        do! writeErrorResponse ctx HttpStatusCode.InternalServerError err
                    | Ok apiKey ->
                        let settings = Config.readSummarySettings ()
                        let! result =
                            NewsSummary.summarizeDetail
                                apiKey settings.Model settings.MaxRetries settings.MaxChars
                                settings.DisableReasoning detail
                            |> Async.StartAsTask
                        match result with
                        | Error err ->
                            do! writeErrorResponse ctx HttpStatusCode.BadGateway err
                        | Ok summary ->
                            match JsonStore.updateSummary Config.reportsJson id summary with
                            | Error err ->
                                Log.warning (sprintf "falha ao salvar resumo [id=%d]: %s" id err)
                                // Ainda assim retorna o resumo gerado ao usuário,
                                // mesmo que a escrita em disco tenha falhado —
                                // só não fica cacheado para a próxima vez.
                                do! writeJsonResponse ctx HttpStatusCode.OK {| summary = summary |}
                            | Ok () ->
                                do! writeJsonResponse ctx HttpStatusCode.OK {| summary = summary |}
    }
```

Ajuste nomes/tipos conforme o que já existe em `Api.fs` (`writeJsonResponse`,
`writeErrorResponse` já são reaproveitados de funções privadas existentes no
arquivo).

## Registrar a rota

Em `configureRoutes`, adicionar (e remover a linha antiga de `/resumos`,
conforme o passo 0):

```fsharp
app.MapGet("/api/news/{id}/summary", handleGetSummary) |> ignore
```

## Considerações

- **Tempo de resposta**: gerar um resumo novo chama a NVIDIA e pode levar
  alguns segundos (mais ainda se houver retry por rate limit). O frontend
  (passos 3 e 4) precisa mostrar um estado de carregamento no popup — não
  trate isso como bug do endpoint.
- **Não há necessidade de paralelismo aqui**: cada requisição resume um único
  comunicado, então não precisa do `MaxParallelism` que existia na feature
  antiga.
- **Erros não são cacheados**: se a extração ou a chamada à NVIDIA falhar, o
  campo `summary` no `reports.json` continua vazio — a próxima tentativa do
  usuário vai gerar de novo, não vai ficar "travado" com um erro salvo.

## Critério de aceite

- `GET /api/news/{id}/summary` para um `id` existente sem resumo ainda:
  gera, salva em `reports.json`, e retorna `{"summary": "..."}`.
- Repetir a mesma chamada retorna o mesmo resumo instantaneamente (sem
  chamar a NVIDIA de novo) — confirmar isso checando os logs/tempo de
  resposta.
- `id` inexistente retorna 404 com mensagem clara.
- Sem `NVIDIA_API_KEY` configurada, retorna erro claro (não trava a request).
