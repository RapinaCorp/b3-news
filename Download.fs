module Download

open System
open System.Net.Http
open System.Text.RegularExpressions
open System.IO

open Thoth.Json.Net

let _outputDir = "report"

let patternFnet =
    "https://fnet.bmfbovespa.com.br/fnet/publico/visualizarDocumento\\?id=([0-9]+)"

let patternEnet =
    "https://www.rad.cvm.gov.br/ENET/frmExibirArquivoIPEExterno.aspx\\?ID=([0-9]+)"

let patternEnetNbc = "(https://www.rad.cvm.gov.br/ENET/frmNBC.aspx\\?NSD=[0-9]+)"

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
    if fnetMatch.Success then
        Fnet(int fnetMatch.Groups.[1].Value)
    elif enetMatch.Success then
        Enet(int enetMatch.Groups.[1].Value)
    elif nbcMatch.Success then
        EnetNbc nbcMatch.Value
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

let rec private getWithRetry (url: string) (attempts: int) =
    async {
        try
            let! resp = httpClient.GetAsync url |> Async.AwaitTask
            return Ok resp
        with ex ->
            let msg = ex.ToString()
            if attempts > 1 && msg.Contains "The SSL connection could not be established" then
                Log.warning (sprintf "GET failed with SSL error, retrying... (%d attempts left)" (attempts - 1))
                do! Async.Sleep 500
                return! getWithRetry url (attempts - 1)
            else
                return Error ex
    }

let rec private postWithRetry (url: string) (content: StringContent) (attempts: int) =
    async {
        try
            let! resp = httpClient.PostAsync(url, content) |> Async.AwaitTask
            return Ok resp
        with ex ->
            let msg = ex.ToString()
            if attempts > 1 && msg.Contains "The SSL connection could not be established" then
                Log.warning (sprintf "POST failed with SSL error, retrying... (%d attempts left)" (attempts - 1))
                do! Async.Sleep 500
                return! postWithRetry url content (attempts - 1)
            else
                return Error ex
    }

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
            let jsonBody =
                $"""{{ "codigoInstituicao": "2", "numeroProtocolo": "{id}", "token": "", "versaoCaptcha": "" }}"""
            use content = new StringContent(jsonBody, Text.Encoding.UTF8, "application/json")
            let! respResult =
                postWithRetry "https://www.rad.cvm.gov.br/ENET/frmExibirArquivoIPEExterno.aspx/ExibirPDF" content 3
            let resp = Result.mapError Network respResult
            let! body = bindAsync readBody resp
            let decoded =
                body
                |> Result.bind (fun text ->
                    match Decode.Auto.fromString<{| d: string |}> text with
                    | Ok json -> decodeBase64 json.d
                    | Error err -> Error(Decode("json", err)))
            return decoded |> Result.map finalize
    }

let save (folder: string) (filename: string) (bytes: byte[]) (kind: Kind) : Async<Result<SaveOutcome, FetchErr>> =
    let ext =
        match kind with
        | Pdf -> ".pdf"
        | Html -> ".html"

    async {
        let filePath = _outputDir +/ folder +/ (filename + ext)
        try
            if File.Exists filePath then
                return Ok(Skipped filePath)
            else
                System.IO.Directory.CreateDirectory(_outputDir +/ folder) |> ignore
                use fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write)
                do! fileStream.WriteAsync(bytes, 0, bytes.Length) |> Async.AwaitTask
                return Ok(Written filePath)
        with ex ->
            return Error(Io ex.Message)
    }

let downloadAndSaveFileAsync (folder: string) (filename: string) (url: string) : Async<Result<SaveOutcome, FetchErr>> =
    async {
        let source = classify url
        let! fetched = fetchBytes source
        match fetched with
        | Error err -> return Error err
        | Ok(bytes, kind) -> return! save folder filename bytes kind
    }

let filesListFilename = _outputDir +/ "files.lst"

let saveFileList (filelist: List<string * string>) =
    let directory = Path.GetDirectoryName filesListFilename

    try
        if not (Directory.Exists directory) then
            Directory.CreateDirectory directory |> ignore

        let lines = filelist |> List.map (fun (ticker, filename) -> ticker + ";" + filename)

        let fileMode =
            if File.Exists filesListFilename then
                FileMode.Append
            else
                FileMode.Create
        use fileStream = new FileStream(filesListFilename, fileMode, FileAccess.Write)
        use streamWriter = new StreamWriter(fileStream)
        lines |> List.iter streamWriter.WriteLine
        streamWriter.Flush()
        fileStream.Flush true
    with
    | :? IOException as ex ->
        Log.error $"Erro de I/O ao salvar a lista de arquivos em '{filesListFilename}': {ex.Message}"
    | ex -> Log.error $"Erro inesperado ao salvar a lista de arquivos: {ex.Message}"

let readFile (filePath: string) : Option<List<string * string>> =
    try
        if File.Exists filePath then
            File.ReadAllLines filePath
            |> Array.distinct
            |> Array.toList
            |> List.map (fun line -> line.Split ';')
            |> List.map (fun arr -> arr.[0], arr.[1])
            |> Some
        else
            Log.warning "arquivo de listagem de arquivos não encontrado"
            None
    with err ->
        Log.error err.Message
        None

let readFileList () = readFile filesListFilename

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
