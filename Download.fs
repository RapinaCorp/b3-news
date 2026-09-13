module Download

open System
open System.Net.Http
open System.Text.RegularExpressions
open System.IO

open Thoth.Json.Net

let patternFnet =
    "https://fnet.bmfbovespa.com.br/fnet/publico/(?:visualizarDocumento|exibirDocumento)\\?id=([0-9]+)"

let patternEnet =
    "https://www.rad.cvm.gov.br/ENET(?:WEB)?/frmExibirArquivoIPEExterno.aspx\\?ID=([0-9]+)"

let patternEnetNbc =
    "(https://www.rad.cvm.gov.br/ENET(?:WEB)?/frmNBC.aspx\\?NSD=[0-9]+)"

/// URLs já transformadas por getRealUrl (proxy local do Enet).
let patternPdfProxy = @"(?:^|/)pdf/(\d+)\.pdf(?:$|\?)"

/// URL do WebMethod que devolve o PDF (base64) — não depende do id, então fica fixa.
let private enetApiUrl =
    "https://www.rad.cvm.gov.br/ENETWEB/frmExibirArquivoIPEExterno.aspx/ExibirPDF"

/// Custom operator for combining paths
let (+/) path1 path2 = Path.Combine(path1, path2)

type Source =
    | Fnet of id: int
    | Enet of id: int
    | EnetNbc of nsd: string
    | Unknown of url: string

type Kind =
    | Pdf
    | Html

type FetchErr =
    | BadUrl of string
    | Network of exn
    | Upstream of status: int * body: string
    | Decode of stage: string * msg: string
    | Rejected of reason: string
    | CaptchaRequired of protocolo: string
    | Io of msg: string

/// Renders a FetchErr as a short, log-safe string. Upstream bodies are already
/// truncated at the FetchErr boundary; exception messages are printed directly
/// (they do not carry upstream payloads).
let describeFetchErr (err: FetchErr) : string =
    match err with
    | BadUrl url -> sprintf "URL inválida: %s" url
    | Network ex -> sprintf "falha de rede: %s" ex.Message
    | Upstream(status, body) -> sprintf "erro do servidor (HTTP %d): %s" status body
    | Decode(stage, msg) -> sprintf "falha ao decodificar %s: %s" stage msg
    | Rejected reason -> sprintf "CVM rejeitou a requisição: %s" reason
    | CaptchaRequired protocolo -> sprintf "CVM exigiu captcha para o protocolo %s (requer navegador)" protocolo
    | Io msg -> sprintf "erro de I/O: %s" msg

/// Maps a FetchErr to an HTTP status for client-facing responses. Upstream
/// errors mirror the remote status; network/decoding/IO failures map to the
/// closest 4xx/5xx.
let httpStatusForFetchErr (err: FetchErr) : int =
    match err with
    | BadUrl _ -> 400
    | Network _ -> 502
    | Upstream(status, _) -> status
    | Decode("json", _) -> 502
    | Decode _ -> 422
    | Rejected _ -> 502
    | CaptchaRequired _ -> 503
    | Io _ -> 500

type SaveOutcome =
    | Written of path: string
    | Skipped of path: string

/// Classifies a B3 download-source URL. Single place where the raw URL patterns
/// are matched; callers pattern-match on `Source` instead of re-running regexes.
let classify (url: string) : Source =
    let fnetMatch = Regex.Match(url, patternFnet)
    let enetMatch = Regex.Match(url, patternEnet)
    let nbcMatch = Regex.Match(url, patternEnetNbc)
    let pdfProxyMatch = Regex.Match(url, patternPdfProxy, RegexOptions.IgnoreCase)
    if fnetMatch.Success then
        Fnet(int fnetMatch.Groups.[1].Value)
    elif enetMatch.Success then
        Enet(int enetMatch.Groups.[1].Value)
    elif nbcMatch.Success then
        EnetNbc nbcMatch.Value
    elif pdfProxyMatch.Success then
        Enet(int pdfProxyMatch.Groups.[1].Value)
    else
        Unknown url

let private httpClient = new HttpClient()

let private truncate (s: string) (maxLen: int) =
    if String.length s <= maxLen then
        s
    else
        s.Substring(0, maxLen)

let private removeQuotes (body: string) : string =
    if body.StartsWith "\"" && body.EndsWith "\"" then
        body[1 .. (max 1 body.Length - 2)] // remove quotes from "body"
    else
        body

/// Retries a transient HTTP op (GET or POST) on SSL handshake failures.
/// Both getWithRetry and postWithRetry are now thin wrappers around this.
let rec private withRetry (attempts: int) (op: unit -> Threading.Tasks.Task<'a>) : Async<Result<'a, exn>> =
    async {
        try
            let! result = op () |> Async.AwaitTask
            return Ok result
        with ex ->
            let msg = ex.ToString()
            if attempts > 1 && msg.Contains "The SSL connection could not be established" then
                Log.warning (sprintf "request failed with SSL error, retrying... (%d attempts left)" (attempts - 1))
                do! Async.Sleep 500
                return! withRetry (attempts - 1) op
            else
                return Error ex
    }

let private getWithRetry (url: string) (attempts: int) =
    withRetry attempts (fun () -> httpClient.GetAsync url)

let private postWithRetry (url: string) (content: StringContent) (headers: (string * string) list) (attempts: int) =
    withRetry attempts (fun () ->
        // Não usar `use`/dispose aqui: req.Dispose() derrubaria `content`, que é
        // reaproveitado entre tentativas de retry.
        let req = new HttpRequestMessage(HttpMethod.Post, url, Content = content)
        headers
        |> List.iter (fun (k, v) -> req.Headers.TryAddWithoutValidation(k, v) |> ignore)
        httpClient.SendAsync req)

/// Content kind of decoded bytes. Semantics match the old `strcasestr "<html>"`
/// sniffing: PDF when the bytes carry the %PDF magic, otherwise HTML when they
/// carry "<html>", defaulting to Pdf (today's ".pdf" fallback).
let detectKind (bytes: byte[]) : Kind =
    let body = System.Text.Encoding.UTF8.GetString bytes
    if body.ToUpper().Contains "%PDF" then Pdf
    elif body.ToUpper().Contains "<HTML>" then Html
    else Pdf

/// Binds an async Result-returning function over a Result, passing errors through.
let private bindAsync (f: 'a -> Async<Result<'b, 'err>>) (r: Result<'a, 'err>) : Async<Result<'b, 'err>> =
    match r with
    | Error err -> async { return Error err }
    | Ok value -> f value

/// Reads the response body, mapping non-success statuses to `Upstream`.
let private readBody (resp: HttpResponseMessage) : Async<Result<string, FetchErr>> =
    async {
        use response = resp
        let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
        if response.IsSuccessStatusCode then
            return Ok content
        else
            return Error(Upstream(int response.StatusCode, truncate content 256))
    }

/// Decodes a base64 string into bytes, mapping failures to `Decode`.
let private decodeBase64 (b64: string) : Result<byte[], FetchErr> =
    try
        Ok(Convert.FromBase64String b64)
    with ex ->
        Error(Decode("base64", ex.Message))

/// Interpreta o corpo JSON `{ "d": ... }` do WebMethod ExibirPDF, distinguindo
/// erro de negócio (":ERRO:..."), exigência de captcha ("V2") e sucesso (base64).
let private parseIpeResponse (protocolo: int) (text: string) : Result<byte[], FetchErr> =
    match Decode.Auto.fromString<{| d: string |}> text with
    | Error err -> Error(Decode("json", err))
    | Ok json when json.d = "V2" -> Error(CaptchaRequired(string protocolo))
    | Ok json when json.d.StartsWith ":ERRO:" -> Error(Rejected(json.d.Substring 6))
    | Ok json when String.IsNullOrWhiteSpace json.d -> Error(Rejected "resposta vazia da CVM")
    | Ok json -> decodeBase64 json.d

/// Fetches the bytes behind a classified B3 source, decoding base64 and
/// classifying content kind. Retries transient SSL failures (3 attempts).
let fetchBytes (source: Source) : Async<Result<byte[] * Kind, FetchErr>> =
    let finalize (bytes: byte[]) = bytes, detectKind bytes
    async {
        match source with
        | Unknown url -> return Error(BadUrl url)
        | EnetNbc nsd -> return Error(BadUrl nsd)
        | Fnet id ->
            let url =
                sprintf "https://fnet.bmfbovespa.com.br/fnet/publico/exibirDocumento?id=%d" id
            let! respResult = getWithRetry url 3
            let resp = Result.mapError Network respResult
            let! body = bindAsync readBody resp
            return body |> Result.bind (decodeBase64 << removeQuotes) |> Result.map finalize
        | Enet id ->
            // A CVM passou a servir uma página wrapper (JS + WebMethod) em vez do PDF
            // direto, e o endpoint migrou de /ENET/ para /ENETWEB/. O WebMethod também
            // parece exigir uma sessão "aquecida" — por isso o GET na página do
            // visualizador antes do POST.
            let viewerUrl =
                sprintf "https://www.rad.cvm.gov.br/ENETWEB/frmExibirArquivoIPEExterno.aspx?ID=%d" id

            let jsonBody =
                $"""{{ "codigoInstituicao": "2", "numeroProtocolo": "{id}", "token": "", "versaoCaptcha": "" }}"""
            use content = new StringContent(jsonBody, Text.Encoding.UTF8, "application/json")
            let headers = [ "X-Requested-With", "XMLHttpRequest" ]

            let! warmup = getWithRetry viewerUrl 3
            let! body =
                Result.mapError Network warmup
                |> bindAsync (fun resp ->
                    resp.Dispose()
                    async {
                        let! respResult = postWithRetry enetApiUrl content headers 3
                        return! bindAsync readBody (Result.mapError Network respResult)
                    })

            return body |> Result.bind (parseIpeResponse id) |> Result.map finalize
    }

/// Returns an HTML <a> for the given url, dispatching on the classified source:
/// fnet links directly to the CVM document, enet links to the local /pdf proxy,
/// everything else links to the raw url.
let getHtmlLink (url: string) (linkText: string) : string =
    match classify url with
    | Fnet id ->
        $"<a href='https://fnet.bmfbovespa.com.br/fnet/publico/exibirDocumento?id={id}' target='_self'>{linkText}</a>"
    | Enet id -> $"<a href='/pdf/{id}.pdf' target='_self'>{linkText}</a>"
    | EnetNbc _
    | Unknown _ -> $"<a href='{url}' target='_self'>{linkText}</a>"

/// Returns the display url for the given raw url, dispatching on the classified
/// source: fnet direct link, enet local proxy, nbc untouched, unknown passthrough.
let getRealUrl (url: string) : string =
    match classify url with
    | Fnet id -> sprintf "https://fnet.bmfbovespa.com.br/fnet/publico/exibirDocumento?id=%d" id
    | Enet id -> $"/pdf/{id}.pdf"
    | EnetNbc nsd -> nsd
    | Unknown _ -> url
