module NvidiaClient

open System
open System.Diagnostics
open System.Net
open System.Net.Http
open System.Text
open System.Threading.Tasks

open Thoth.Json.Net

let endpoint = "https://integrate.api.nvidia.com/v1/chat/completions"

let defaultTemperature = 0.3
let defaultMaxTokens = 512

type ChatMessage = { role: string; content: string }

type private ChatChoiceMessage = { content: string }

type private ChatChoice = { message: ChatChoiceMessage }

type private ChatResponse = { choices: ChatChoice list }

let private httpClient =
    let c = new HttpClient()
    c.Timeout <- TimeSpan.FromSeconds 90.0
    c

/// Delay before retrying a 429. Uses Retry-After when present, otherwise
/// exponential backoff starting at 1s (1, 2, 4, ...), capped at 30s.
let delayFor429 (retryAfter: TimeSpan option) (attempt: int) : TimeSpan =
    let fromHeader =
        retryAfter
        |> Option.filter (fun t -> t > TimeSpan.Zero && t < TimeSpan.FromMinutes 2.0)
    match fromHeader with
    | Some t -> t
    | None ->
        let seconds = min 30.0 (Math.Pow(2.0, float (max 0 attempt)))
        TimeSpan.FromSeconds seconds

let parseChatContent (json: string) : Result<string, string> =
    match Decode.Auto.fromString<ChatResponse> json with
    | Error err -> Error(sprintf "resposta NVIDIA inválida: %s" err)
    | Ok resp when List.isEmpty resp.choices -> Error "resposta NVIDIA sem choices"
    | Ok resp ->
        let text = resp.choices.Head.message.content
        if String.IsNullOrWhiteSpace text then
            Error "resposta NVIDIA vazia"
        else
            Ok(text.Trim())

let private encodeRequest (model: string) (systemPrompt: string) (content: string) : string =
    let messages =
        Encode.list
            [ Encode.object [ "role", Encode.string "system"; "content", Encode.string systemPrompt ]
              Encode.object [ "role", Encode.string "user"; "content", Encode.string content ] ]
    Encode.object
        [ "model", Encode.string model
          "messages", messages
          "temperature", Encode.float defaultTemperature
          "max_tokens", Encode.int defaultMaxTokens ]
    |> Encode.toString 0

let private retryAfterOf (resp: HttpResponseMessage) : TimeSpan option =
    let ra = resp.Headers.RetryAfter
    if isNull ra then
        None
    elif ra.Delta.HasValue then
        Some ra.Delta.Value
    elif ra.Date.HasValue then
        let delta = ra.Date.Value - DateTimeOffset.UtcNow
        if delta > TimeSpan.Zero then Some delta else None
    else
        None

let rec private postWithRetry
    (apiKey: string)
    (model: string)
    (systemPrompt: string)
    (content: string)
    (label: string)
    (attemptsLeft: int)
    (attempt: int)
    : Async<Result<string, string>> =
    async {
        let sw = Stopwatch.StartNew()
        try
            use req = new HttpRequestMessage(HttpMethod.Post, endpoint)
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey) |> ignore
            req.Content <- new StringContent(encodeRequest model systemPrompt content, Encoding.UTF8, "application/json")
            use! resp = httpClient.SendAsync req |> Async.AwaitTask
            let! body = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
            sw.Stop()
            let status = int resp.StatusCode
            if resp.StatusCode = HttpStatusCode.TooManyRequests && attemptsLeft > 1 then
                let delay = delayFor429 (retryAfterOf resp) attempt
                Log.warning (
                    sprintf
                        "NVIDIA 429 (%s, %d chars, %dms), retry em %dms (%d tentativas restantes)"
                        label
                        content.Length
                        sw.ElapsedMilliseconds
                        (int delay.TotalMilliseconds)
                        (attemptsLeft - 1)
                )
                do! Async.Sleep(int delay.TotalMilliseconds)
                return! postWithRetry apiKey model systemPrompt content label (attemptsLeft - 1) (attempt + 1)
            elif not resp.IsSuccessStatusCode then
                Log.warning (
                    sprintf
                        "NVIDIA falhou (%s, %d chars, HTTP %d, %dms)"
                        label
                        content.Length
                        status
                        sw.ElapsedMilliseconds
                )
                let snippet =
                    if body.Length <= 256 then body else body.Substring(0, 256)
                return Error(sprintf "HTTP %d da NVIDIA: %s" status snippet)
            else
                match parseChatContent body with
                | Ok text ->
                    Log.info (
                        sprintf
                            "NVIDIA ok (%s, %d chars, %dms)"
                            label
                            content.Length
                            sw.ElapsedMilliseconds
                    )
                    return Ok text
                | Error err ->
                    Log.warning (sprintf "NVIDIA parse falhou (%s, %dms): %s" label sw.ElapsedMilliseconds err)
                    return Error err
        with
        | :? TaskCanceledException ->
            sw.Stop()
            Log.warning (sprintf "NVIDIA timeout (%s, %d chars, %dms)" label content.Length sw.ElapsedMilliseconds)
            return Error "timeout na API da NVIDIA"
        | ex ->
            sw.Stop()
            Log.warning (sprintf "NVIDIA erro de rede (%s): %s" label ex.Message)
            return Error(sprintf "falha de rede NVIDIA: %s" ex.Message)
    }

/// Envia um texto para o modelo e retorna o resumo gerado.
/// `label` é só para log (ex: ticker); não vai na API. `maxRetries` inclui a primeira tentativa.
let summarize (apiKey: string) (model: string) (systemPrompt: string) (content: string) (label: string) (maxRetries: int) : Async<Result<string, string>> =
    let attempts = max 1 maxRetries
    Log.info (sprintf "NVIDIA summarize: %s chars=%d" label content.Length)
    postWithRetry apiKey model systemPrompt content label attempts 0
