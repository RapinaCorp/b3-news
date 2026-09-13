module ContentExtractor

open System
open System.IO
open System.Net.Http
open System.Text
open System.Text.RegularExpressions
open System.Threading.Tasks

open HtmlAgilityPack
open UglyToad.PdfPig

open Download

type ExtractedContent =
    { Url: string
      Text: string
      SourceType: string }

let private httpClient =
    let c = new HttpClient()
    c.Timeout <- TimeSpan.FromSeconds 60.0
    c

let truncate (s: string) (maxChars: int) =
    if maxChars <= 0 || s.Length <= maxChars then
        s
    else
        s.Substring(0, maxChars)

let private collapseWs (s: string) =
    Regex.Replace(s, @"\s+", " ").Trim()

let extractHtml (bytes: byte[]) (maxChars: int) : Result<string, string> =
    try
        let html = Encoding.UTF8.GetString bytes
        let doc = HtmlDocument()
        doc.LoadHtml html
        let junk = doc.DocumentNode.SelectNodes("//script|//style|//nav|//footer|//noscript|//header")
        if not (isNull junk) then
            for n in junk do
                n.Remove()
        let raw =
            if isNull doc.DocumentNode then
                ""
            else
                HtmlEntity.DeEntitize(doc.DocumentNode.InnerText)
        let text = collapseWs raw |> fun t -> truncate t maxChars
        if String.IsNullOrWhiteSpace text then
            Error "HTML sem texto extraível"
        else
            Ok text
    with ex ->
        Error(sprintf "falha ao extrair HTML: %s" ex.Message)

let extractPdf (bytes: byte[]) (maxChars: int) : Result<string, string> =
    try
        use doc = PdfDocument.Open(new MemoryStream(bytes))
        let raw =
            doc.GetPages()
            |> Seq.map (fun p -> p.Text)
            |> String.concat " "
        let text = collapseWs raw |> fun t -> truncate t maxChars
        if String.IsNullOrWhiteSpace text then
            Error "PDF sem texto extraível"
        else
            Ok text
    with ex ->
        Error(sprintf "falha ao extrair PDF: %s" ex.Message)

let extractFromBytes (url: string) (kind: Kind) (bytes: byte[]) (maxChars: int) : Result<ExtractedContent, string> =
    let parsed =
        match kind with
        | Html -> extractHtml bytes maxChars |> Result.map (fun t -> t, "html")
        | Pdf -> extractPdf bytes maxChars |> Result.map (fun t -> t, "pdf")
    parsed
    |> Result.map (fun (text, sourceType) ->
        { Url = url
          Text = text
          SourceType = sourceType })

let private kindFromContentType (contentType: string) (url: string) : Kind =
    let ct = if isNull contentType then "" else contentType.ToLowerInvariant()
    if ct.Contains "pdf" then
        Pdf
    elif ct.Contains "html" || ct.Contains "text/plain" then
        Html
    elif url.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) then
        Pdf
    elif url.EndsWith(".htm", StringComparison.OrdinalIgnoreCase)
         || url.EndsWith(".html", StringComparison.OrdinalIgnoreCase) then
        Html
    else
        Pdf

let private fetchGeneric (url: string) : Async<Result<byte[] * Kind, string>> =
    async {
        try
            use! resp = httpClient.GetAsync url |> Async.AwaitTask
            let! bytes = resp.Content.ReadAsByteArrayAsync() |> Async.AwaitTask
            if not resp.IsSuccessStatusCode then
                let snippet =
                    let s = Encoding.UTF8.GetString bytes
                    if s.Length <= 256 then s else s.Substring(0, 256)
                return Error(sprintf "HTTP %d ao baixar %s: %s" (int resp.StatusCode) url snippet)
            else
                let media =
                    if isNull resp.Content.Headers.ContentType then
                        ""
                    else
                        string resp.Content.Headers.ContentType
                let kind =
                    match detectKind bytes with
                    | Html -> Html
                    | Pdf when media.ToLowerInvariant().Contains "html" -> Html
                    | Pdf -> kindFromContentType media url
                return Ok(bytes, kind)
        with
        | :? TaskCanceledException -> return Error(sprintf "timeout ao baixar %s" url)
        | ex -> return Error(sprintf "falha de rede ao baixar %s: %s" url ex.Message)
    }

/// Baixa o comunicado (reusa Download.fetchBytes para Fnet/Enet) e devolve texto plano truncado.
let extractText (url: string) (maxChars: int) : Async<Result<ExtractedContent, string>> =
    async {
        match classify url with
        | Unknown u ->
            let! fetched = fetchGeneric u
            match fetched with
            | Error err -> return Error err
            | Ok(bytes, kind) -> return extractFromBytes url kind bytes maxChars
        | EnetNbc nsd ->
            let! fetched = fetchGeneric nsd
            match fetched with
            | Error err -> return Error err
            | Ok(bytes, kind) -> return extractFromBytes url kind bytes maxChars
        | source ->
            let! fetched = fetchBytes source
            match fetched with
            | Error err -> return Error(describeFetchErr err)
            | Ok(bytes, kind) -> return extractFromBytes url kind bytes maxChars
    }
