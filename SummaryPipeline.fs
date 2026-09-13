module SummaryPipeline

open System

open Details

type ComunicadoResumo =
    { Data: DateTime
      Titulo: string
      Url: string
      Resumo: string }

type AtivoResumo =
    { Ativo: string
      Comunicados: ComunicadoResumo list
      ResumoConsolidado: string
      Falhas: int }

let individualSystemPrompt =
    "Resuma o comunicado abaixo em até 4 linhas, focando em:\n\
     o que foi decidido/anunciado, valores relevantes, e impacto pro acionista.\n\
     Não repita o texto, seja direto."

let consolidatedSystemPrompt (ativo: string) =
    sprintf
        "Você recebeu resumos dos últimos comunicados da empresa %s.\n\
         Produza um resumo executivo dos principais pontos, em ordem cronológica,\n\
         destacando o que é mais relevante para decisão de investimento."
        ativo

let parseDetailDate (s: string) : DateTime =
    match DateTime.TryParse s with
    | true, dt -> dt
    | false, _ -> DateTime.MinValue

/// Últimos `n` comunicados por ticker, mais recentes primeiro.
let selectRecentDocuments (details: NewsDetail list) (tickers: string list) (n: int) : (string * NewsDetail list) list =
    let wanted =
        tickers
        |> List.map (fun t -> t.Trim().ToUpperInvariant())
        |> List.filter (fun t -> t <> "")
        |> List.distinct
    wanted
    |> List.map (fun ticker ->
        let docs =
            details
            |> List.filter (fun d -> d.ticker.Trim().ToUpperInvariant() = ticker)
            |> List.sortByDescending (fun d -> d.dateTime)
            |> List.truncate (max 0 n)
        ticker, docs)

type ExtractFn = string -> int -> Async<Result<ContentExtractor.ExtractedContent, string>>
type SummarizeFn = string -> string -> string -> Async<Result<string, string>>

let private summarizeOne
    (extract: ExtractFn)
    (summarize: SummarizeFn)
    (maxChars: int)
    (detail: NewsDetail)
    : Async<Result<ComunicadoResumo, string>> =
    async {
        let! extracted = extract detail.url maxChars
        match extracted with
        | Error err ->
            Log.warning (sprintf "extração falhou [%s] %s: %s" detail.ticker detail.url err)
            return Error err
        | Ok content ->
            let userText =
                sprintf "Título: %s\nData: %s\n\n%s" detail.reportType detail.dateTime content.Text
            let! summary = summarize individualSystemPrompt userText detail.ticker
            match summary with
            | Error err ->
                Log.warning (sprintf "resumo falhou [%s] %s: %s" detail.ticker detail.url err)
                return Error err
            | Ok text ->
                return
                    Ok
                        { Data = parseDetailDate detail.dateTime
                          Titulo = detail.reportType
                          Url = detail.url
                          Resumo = text }
    }

let private concatenateForAggregate (ativo: string) (items: ComunicadoResumo list) =
    items
    |> List.sortBy (fun c -> c.Data)
    |> List.map (fun c -> sprintf "[%s] %s\n%s" (c.Data.ToString("yyyy-MM-dd")) c.Titulo c.Resumo)
    |> String.concat "\n\n"
    |> fun body -> sprintf "Ativo: %s\n\n%s" ativo body

let private summarizeAtivo
    (extract: ExtractFn)
    (summarize: SummarizeFn)
    (maxChars: int)
    (maxParallelism: int)
    (ativo: string)
    (docs: NewsDetail list)
    : Async<AtivoResumo> =
    async {
        if List.isEmpty docs then
            return
                { Ativo = ativo
                  Comunicados = []
                  ResumoConsolidado = "Nenhum comunicado recente encontrado para este ativo."
                  Falhas = 0 }
        else
            let! results =
                docs
                |> List.map (summarizeOne extract summarize maxChars)
                |> fun ops -> Async.Parallel(ops, max 1 maxParallelism)
            let ok, errors =
                results
                |> Array.fold
                    (fun (accOk, accErr) r ->
                        match r with
                        | Ok item -> item :: accOk, accErr
                        | Error _ -> accOk, accErr + 1)
                    ([], 0)
            let comunicados = ok |> List.sortByDescending (fun c -> c.Data)
            let! consolidado =
                async {
                    if List.length comunicados < 5 then
                        return "Resumo consolidado não gerado: menos de 5 comunicados disponíveis."
                    else
                        let payload = concatenateForAggregate ativo comunicados
                        let! agg = summarize (consolidatedSystemPrompt ativo) payload ativo
                        match agg with
                        | Ok text -> return text
                        | Error err ->
                            Log.warning (sprintf "resumo consolidado falhou [%s]: %s" ativo err)
                            return "Falha ao gerar o resumo consolidado; veja os comunicados individuais abaixo."
                }
            let nota =
                if errors = 0 then
                    consolidado
                else
                    consolidado
                    + sprintf "\n\n(%d comunicado(s) não puderam ser processados.)" errors
            return
                { Ativo = ativo
                  Comunicados = comunicados
                  ResumoConsolidado = nota
                  Falhas = errors }
    }

let runWith
    (extract: ExtractFn)
    (summarize: SummarizeFn)
    (maxChars: int)
    (maxParallelism: int)
    (details: NewsDetail list)
    (tickers: string list)
    (maxDocuments: int)
    : Async<AtivoResumo list> =
    async {
        let groups = selectRecentDocuments details tickers maxDocuments
        let! ativos =
            groups
            |> List.map (fun (ativo, docs) ->
                summarizeAtivo extract summarize maxChars maxParallelism ativo docs)
            |> fun ops -> Async.Parallel(ops, 1)
        return ativos |> Array.toList
    }

let run
    (apiKey: string)
    (model: string)
    (maxRetries: int)
    (maxChars: int)
    (maxParallelism: int)
    (details: NewsDetail list)
    (tickers: string list)
    (maxDocuments: int)
    : Async<AtivoResumo list> =
    let summarizePrompt systemPrompt content label =
        NvidiaClient.summarize apiKey model systemPrompt content label maxRetries
    runWith ContentExtractor.extractText summarizePrompt maxChars maxParallelism details tickers maxDocuments
