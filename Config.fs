module Config

open System
open System.IO
open System.Text.Json

let configFileName = "b3news.settings.json"
let reportsJson = "reports.json"

/// Primary env var for the OpenAI-compatible API key. `OPENAI_API_KEY` is
/// also accepted (same value works); `NVIDIA_API_KEY` is kept as a legacy
/// fallback so existing setups keep working.
let llmApiKeyEnv = "LLM_API_KEY"
let openaiApiKeyEnv = "OPENAI_API_KEY"
let nvidiaApiKeyEnv = "NVIDIA_API_KEY"

/// Default OpenAI-compatible chat-completions endpoint (NVIDIA NIM).
/// Override via `Summary.Endpoint` to use OpenAI, Gemini
/// (`https://generativelanguage.googleapis.com/v1beta/openai/chat/completions`),
/// or any other OpenAI-compatible provider — no code change needed.
let defaultLlmEndpoint = "https://integrate.api.nvidia.com/v1/chat/completions"

let (+/) path1 path2 = Path.Combine(path1, path2)

type SummarySettings =
    { Endpoint: string
      Model: string
      MaxChars: int
      MaxRetries: int
      DisableReasoning: bool }

let defaultSummarySettings =
    { Endpoint = defaultLlmEndpoint
      Model = "meta/llama-3.3-70b-instruct"
      MaxChars = 7000
      MaxRetries = 5
      DisableReasoning = false }

let private tryReadInt (el: JsonElement) (name: string) =
    match el.TryGetProperty name with
    | true, v when v.ValueKind = JsonValueKind.Number ->
        match v.TryGetInt32() with
        | true, n -> Some n
        | _ -> None
    | _ -> None

let private tryReadString (el: JsonElement) (name: string) =
    match el.TryGetProperty name with
    | true, v when v.ValueKind = JsonValueKind.String ->
        let s = v.GetString()
        if String.IsNullOrWhiteSpace s then None else Some s
    | _ -> None

let private tryReadBool (el: JsonElement) (name: string) =
    match el.TryGetProperty name with
    | true, v when v.ValueKind = JsonValueKind.True -> Some true
    | true, v when v.ValueKind = JsonValueKind.False -> Some false
    | _ -> None

let private mergeSummary (base': SummarySettings) (el: JsonElement) =
    { Endpoint = tryReadString el "Endpoint" |> Option.defaultValue base'.Endpoint
      Model = tryReadString el "Model" |> Option.defaultValue base'.Model
      MaxChars = tryReadInt el "MaxChars" |> Option.defaultValue base'.MaxChars
      MaxRetries = tryReadInt el "MaxRetries" |> Option.defaultValue base'.MaxRetries
      DisableReasoning = tryReadBool el "DisableReasoning" |> Option.defaultValue base'.DisableReasoning }

let private loadSettingsDoc () : JsonDocument option =
    let appSettingsPath = Path.Combine(Directory.GetCurrentDirectory(), configFileName)
    if not (File.Exists appSettingsPath) then
        None
    else
        try
            Some(JsonDocument.Parse(File.ReadAllText appSettingsPath))
        with _ ->
            None

let readTickersFromFile () : string list =
    match loadSettingsDoc () with
    | None -> []
    | Some doc ->
        use _ = doc
        match doc.RootElement.TryGetProperty "Tickers" with
        | true, tickersElem when tickersElem.ValueKind = JsonValueKind.Array ->
            tickersElem.EnumerateArray()
            |> Seq.filter (fun v -> v.ValueKind = JsonValueKind.String)
            |> Seq.map (fun v -> v.GetString())
            |> Seq.filter (fun s -> not (isNull s))
            |> Seq.toList
        | _ -> []

let readSummarySettings () : SummarySettings =
    match loadSettingsDoc () with
    | None -> defaultSummarySettings
    | Some doc ->
        use _ = doc
        match doc.RootElement.TryGetProperty "Summary" with
        | true, el when el.ValueKind = JsonValueKind.Object -> mergeSummary defaultSummarySettings el
        | _ -> defaultSummarySettings

let private apiKeyFromEnv (name: string) : string option =
    match Environment.GetEnvironmentVariable name with
    | null
    | "" -> None
    | key when String.IsNullOrWhiteSpace key -> None
    | key -> Some(key.Trim())

/// Chave da API OpenAI-compatible (obrigatória, não versionar/logar o valor).
/// Ordem: `LLM_API_KEY`, depois `OPENAI_API_KEY`, depois `NVIDIA_API_KEY`
/// (legado — setups existentes continuam funcionando).
let llmApiKey () : Result<string, string> =
    let foundKey = 
        [ llmApiKeyEnv; openaiApiKeyEnv; nvidiaApiKeyEnv ]
        |> List.tryPick (fun envName -> 
            apiKeyFromEnv envName |> Option.map (fun key -> envName, key))

    match foundKey with
    | Some (envName, key) -> 
        Log.info $"Usando API key da env var: {envName}"
        Ok key
    | None ->
        Error $"Nenhuma chave de API definida. Exporte {llmApiKeyEnv} (ou {openaiApiKeyEnv}, ou {nvidiaApiKeyEnv} legado) com a chave do provedor."

/// Legado: mantido para compatibilidade; prefira `llmApiKey`.
let nvidiaApiKey () : Result<string, string> = llmApiKey ()
