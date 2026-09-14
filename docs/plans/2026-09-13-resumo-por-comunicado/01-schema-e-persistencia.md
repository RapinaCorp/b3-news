# Passo 1 — Campo `summary`, persistência segura e módulo de resumo individual

## 1. Adicionar o campo `summary` a `NewsDetail`

Em `Details.fs`:

```fsharp
type NewsDetail =
    { id: int
      ticker: string
      dateTime: string
      filename: string
      reportType: string
      url: string
      summary: string }
```

Usar `string` simples (não `string option`) — o projeto não tem um conversor
de tipo `option` do F# configurado no `System.Text.Json`; um campo ausente no
JSON já vira `null`/`""` automaticamente na desserialização, sem exigir
nenhuma configuração extra. Trate `""` (ou `null`) como "ainda não resumido".

Atualizar o único local que constrói um `NewsDetail` literal
(`nwsMsgToNewsDetail`, dentro de `Details.fs`) para incluir `summary = ""`.

Itens já existentes em `reports.json` (sem o campo `summary`) devem continuar
sendo lidos normalmente — `JsonStore.tryRead` usa
`JsonSerializerOptions(PropertyNameCaseInsensitive = true)`, e um campo
ausente em um record F# desserializado via construtor recebe o valor default
do tipo (`""` para `string`). Confirme isso rodando `update-json` ou lendo o
`reports.json` existente após a mudança, sem apagar nada manualmente.

## 2. Função de atualização segura em `JsonStore.fs`

O `serve-api` agora escreve em `reports.json` (não só lê), e pode receber
requisições concorrentes de usuários diferentes clicando no chevron ao mesmo
tempo. Adicionar:

```fsharp
let private writeLock = obj ()

/// Atualiza o campo `summary` do item com o `id` informado, relendo o
/// arquivo na hora (não usa uma cópia antiga carregada antes) para minimizar
/// o risco de sobrescrever mudanças feitas por outro processo (ex: um
/// `update-json` rodando ao mesmo tempo). Serializa escritas concorrentes
/// dentro do próprio processo com `writeLock`.
let updateSummary (jsonPath: string) (id: int) (summary: string) : Result<unit, string> =
    lock writeLock (fun () ->
        tryRead jsonPath
        |> Result.bind (fun details ->
            let found = details |> List.exists (fun d -> d.id = id)
            if not found then
                Error (sprintf "Item com id=%d não encontrado em %s." id jsonPath)
            else
                details
                |> List.map (fun d -> if d.id = id then { d with summary = summary } else d)
                |> tryWrite jsonPath))
```

`tryWrite` já existe no arquivo como `private` — como `updateSummary` está no
mesmo módulo, consegue chamá-la diretamente. Ajuste a assinatura acima se o
nome ou a posição de `tryWrite`/`tryRead` for diferente depois do passo 0.

Esse `lock` só protege contra concorrência **dentro do processo
`serve-api`** (múltiplas requisições HTTP simultâneas). Ele não impede uma
colisão com o processo `update-json` rodando em paralelo como CLI separado —
isso é um risco aceito (baixa probabilidade, já que `update-json` roda em
lote/cron e não fica escrevendo continuamente); não precisa resolver isso
agora, só esteja ciente.

## 3. Novo módulo `NewsSummary.fs`

Criar um módulo pequeno, dedicado a resumir **um único comunicado**, reusando
os prompts que já existiam em `SummaryPipeline.fs` (recupere o texto do
prompt do arquivo removido no passo 0 antes de apagá-lo, se ainda não tiver):

```fsharp
module NewsSummary

open Details

let private withNoThink (disableReasoning: bool) (prompt: string) =
    if disableReasoning then "/no_think\n" + prompt else prompt

let individualSystemPrompt (disableReasoning: bool) =
    withNoThink
        disableReasoning
        "Summarize the communication below in up to 4 lines, in Portuguese, focusing on:\n\
         what was decided/announced, relevant values, and impact for the shareholder.\n\
         Do not repeat the text, be direct."

/// Extrai o conteúdo do link do comunicado e gera o resumo em texto.
/// Não decide o que fazer com o resultado (persistir, responder HTTP) —
/// isso fica a cargo de quem chama (o handler da API, no passo 2).
let summarizeDetail
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
        | Error err -> return Error (sprintf "Falha ao extrair conteúdo: %s" err)
        | Ok content ->
            let userText =
                sprintf "Título: %s\nData: %s\n\n%s" detail.reportType detail.dateTime content.Text
            let! summary =
                NvidiaClient.summarize
                    apiKey model (individualSystemPrompt disableReasoning) userText detail.ticker
                    maxRetries disableReasoning
            match summary with
            | Error err -> return Error (sprintf "Falha ao gerar resumo: %s" err)
            | Ok text -> return Ok text
    }
```

Confira a assinatura exata de `NvidiaClient.summarize` (ordem dos parâmetros
e nomes) olhando o arquivo atual antes de copiar a chamada acima — o trecho
serve como referência de intenção, não como código a colar sem revisar.

## Critério de aceite

- `dotnet build` compila sem erros.
- Ler um `reports.json` existente (gerado antes desta mudança) continua
  funcionando — todos os itens aparecem com `summary = ""`.
- Chamar `JsonStore.updateSummary` manualmente (ex: num teste rápido ou REPL)
  para um `id` existente atualiza só aquele item, sem alterar os demais.
