module NewsSummary

open Details

/// Nemotron 3 Super honors `/no_think` in the system prompt to skip the
/// chain-of-thought trace and return only the final answer. Other models
/// ignore the directive as plain text, so it is gated behind `DisableReasoning`.
let private withNoThink (disableReasoning: bool) (prompt: string) =
    if disableReasoning then "/no_think\n" + prompt else prompt

let individualSystemPrompt (disableReasoning: bool) =
    withNoThink
        disableReasoning
        "Summarize the communication below in up to 5 lines, single paragraph, in Portuguese, focusing on:\n\
         what was decided/announced, relevant values, and impact for the shareholder.\n\
         Do not repeat the text, be direct."

/// Extrai o conteúdo do link do comunicado e gera o resumo em texto.
/// `endpoint` é a URL do endpoint OpenAI-compatible (`/chat/completions`).
/// Não decide o que fazer com o resultado (persistir, responder HTTP) —
/// isso fica a cargo de quem chama (o handler da API, no passo 2).
let summarizeDetail
    (endpoint: string)
    (apiKey: string)
    (model: string)
    (maxRetries: int)
    (maxChars: int)
    (disableReasoning: bool)
    (detail: NewsDetail)
    : Async<Result<string, string>> =
    async {
        let! extracted = ContentExtractor.extractText detail.url maxChars
        match extracted with
        | Error err -> return Error(sprintf "Falha ao extrair conteúdo: %s" err)
        | Ok content ->
            let userText =
                sprintf "Título: %s\nData: %s\n\n%s" detail.reportType detail.dateTime content.Text
            let! summary =
                OpenAIClient.summarize
                    endpoint
                    apiKey
                    model
                    (individualSystemPrompt disableReasoning)
                    userText
                    detail.ticker
                    maxRetries
                    disableReasoning
            match summary with
            | Error err -> return Error(sprintf "Falha ao gerar resumo: %s" err)
            | Ok text -> return Ok text
    }
