module Details

open System
open System.Net.Http
open System.Text.RegularExpressions
open System.IO
open System.Security.Cryptography
open System.Text
open System.IO.Compression
open System.Text.Json

open Lib
open News

type NewsDetail =
    { ticker: string
      dateTime: string
      filename: string
      url: string }

let detailsCacheDir = "news_cache"

let getDetailsCacheFileName (newsID: string) (agency: string) (dt: string) =
    let key = newsID + "_" + agency + "_" + dt
    let hash =
        use sha = SHA256.Create()
        let bytes = Encoding.UTF8.GetBytes(key)
        sha.ComputeHash(bytes)
        |> Array.map (fun b -> b.ToString("x2"))
        |> String.concat ""
    Path.Combine(detailsCacheDir, hash + ".json")

let writeGzip (path: string) (content: string) =
    use fs = new FileStream(path, FileMode.Create, FileAccess.Write)
    use gz = new GZipStream(fs, CompressionMode.Compress)
    let bytes = Encoding.UTF8.GetBytes(content)
    gz.Write(bytes, 0, bytes.Length)

let readGzip (path: string) : string option =
    try
        use fs = new FileStream(path, FileMode.Open, FileAccess.Read)
        use gz = new GZipStream(fs, CompressionMode.Decompress)
        use ms = new MemoryStream()
        gz.CopyTo(ms)
        let bytes = ms.ToArray()
        Some(Encoding.UTF8.GetString(bytes))
    with _ ->
        None

let readGzipOrPlainTextAsync (filePath: string) =
    async {
        use fs = File.OpenRead(filePath)
        let isGzip =
            if fs.Length > 2L then
                let b1 = fs.ReadByte()
                let b2 = fs.ReadByte()
                fs.Seek(0L, SeekOrigin.Begin) |> ignore
                b1 = 0x1f && b2 = 0x8b
            else
                false
        if isGzip then
            use gz = new GZipStream(fs, CompressionMode.Decompress)
            use sr = new StreamReader(gz, Encoding.UTF8)
            let! text = sr.ReadToEndAsync() |> Async.AwaitTask
            return text
        else
            use sr = new StreamReader(fs, Encoding.UTF8)
            let! text = sr.ReadToEndAsync() |> Async.AwaitTask
            return text
    }

let writeGzipAsync (filePath: string) (content: string) =
    async {
        use fs = File.Open(filePath, FileMode.Create, FileAccess.Write)
        use gz = new GZipStream(fs, CompressionMode.Compress)
        use sw = new StreamWriter(gz, Encoding.UTF8)
        do! sw.WriteAsync(content) |> Async.AwaitTask
    }

let fetchDetails (newsID: string) (agency: string) (dt: string) =
    async {
        Directory.CreateDirectory(detailsCacheDir) |> ignore
        let cacheFile = getDetailsCacheFileName newsID agency dt
        if File.Exists(cacheFile) then
            // Try reading as gzip first, fallback to plain text
            let cached =
                match readGzip cacheFile with
                | Some s -> s
                | None -> File.ReadAllText(cacheFile)
            // Try to deserialize cached response
            let resp = new System.Net.Http.HttpResponseMessage()
            resp.Content <- new System.Net.Http.StringContent(cached)
            resp.StatusCode <- System.Net.HttpStatusCode.OK
            return resp
        else
            let url =
                "https://sistemasweb.b3.com.br/PlantaoNoticias/Noticias/Detail?idNoticia="
                + Uri.EscapeDataString(newsID)
                + "&agencia="
                + Uri.EscapeDataString(agency)
                + "&dataNoticia="
                + Uri.EscapeDataString(dt)
            use httpClient = new HttpClient()
            let! response = httpClient.GetAsync(url) |> Async.AwaitTask
            if response.IsSuccessStatusCode then
                let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                writeGzip cacheFile content
            return response
    }

let getURL (resp: HttpResponseMessage) : string =
    let txt = resp.Content.ReadAsStringAsync().Result
    let regexPattern = "(http[s]*://[^\\?]+\\?[Ii][Dd]=[0-9]+)"
    let matchResult = Regex.Match(txt, regexPattern)
    matchResult.Value

let nwsMsgToNewsDetail (nwsmsg: News.NwsMsg) : Async<Result<NewsDetail, string>> =
    async {
        let nid = string (nwsmsg.id)
        let nidAgencia = string (nwsmsg.IdAgencia)
        let ndateTime = nwsmsg.dateTime

        let! details = fetchDetails nid nidAgencia ndateTime
        let status = details.StatusCode
        let code = int status
        let msg = status.ToString()

        // printfn "%d => %s (%s) [%s] %s" code ndateTime nidAgencia msg nwsmsg.headline

        if code < 200 || code >= 300 then
            return (Error("[" + code.ToString() + "] " + msg + ": " + string (nwsmsg.headline)))
        else
            match ndateTime + "-" + nwsmsg.headline |> safeFilename with
            | None -> return (Error "nome do arquivo não disponível")
            | Some filename ->
                return
                    Ok(
                        { ticker = nwsmsg.ticker |> Option.defaultValue ""
                          dateTime = ndateTime
                          filename = filename
                          url = getURL details }
                    )
    }

let transform (f: 'a -> Async<Result<'b, string>>) (list: 'a list) : Result<'b list, string> =
    let rec sequenceResults acc =
        function
        | [] -> Ok(List.rev acc)
        | x :: xs ->
            match f x with
            | Success(Ok value) -> sequenceResults (value :: acc) xs
            | Success(Error errorMessage) -> Error errorMessage
            | Failure ex -> Error ex.Message

    sequenceResults [] list

let fetchNewsDetails (codes: string list) (d1: string) (d2: string) : Result<NewsDetail list, string> =
    let result = News.fetchAndFilterNews codes d1 d2

    match result with
    | News.Success nwsmsgs ->
        match nwsmsgs with
        | Ok nwsmsgs' -> transform nwsMsgToNewsDetail nwsmsgs'
        | Error errorMessage -> Error errorMessage
    | News.Failure ex -> Error ex.Message
