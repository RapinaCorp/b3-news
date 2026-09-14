module Config

open System
open System.IO
open System.Text.Json

let configFileName = "b3news.settings.json"
let reportsJson = "reports.json"
let nvidiaApiKeyEnv = "NVIDIA_API_KEY"

let (+/) path1 path2 = Path.Combine(path1, path2)

type SummarySettings =
    { Model: string
      MaxChars: int
      MaxRetries: int
      DisableReasoning: bool }

let defaultSummarySettings =
    { Model = "meta/llama-3.3-70b-instruct"
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
    { Model = tryReadString el "Model" |> Option.defaultValue base'.Model
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

let nvidiaApiKey () : Result<string, string> =
    match Environment.GetEnvironmentVariable nvidiaApiKeyEnv with
    | null
    | "" ->
        Error(
            sprintf
                "%s não definida. Exporte a variável de ambiente com a chave (formato nvapi-...)."
                nvidiaApiKeyEnv
        )
    | key when String.IsNullOrWhiteSpace key ->
        Error(sprintf "%s está vazia." nvidiaApiKeyEnv)
    | key -> Ok(key.Trim())
