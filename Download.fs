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

/// Custom operator for combining paths
let (+/) path1 path2 = Path.Combine(path1, path2)

let fnetURL (url: string) : string option =
    let pattern = patternFnet
    let urlMatch = Regex.Match(url, pattern).Groups.[1].Value

    let newUrl =
        "https://fnet.bmfbovespa.com.br/fnet/publico/exibirDocumento?id="
        + urlMatch
        + "&#toolbar=0"

    if String.IsNullOrEmpty(urlMatch) then
        None
    else
        Some(newUrl)

/// fnet returns the body (html) from the requested page
let fnet (url: string) : Async<Result<string, string>> =
    async {
        match fnetURL url with
        | None -> return Error "URL não encontrada"
        | Some(newUrl) ->
            try
                use httpClient = new HttpClient()
                let! response = httpClient.GetAsync(newUrl) |> Async.AwaitTask
                let! content = response.Content.ReadAsStreamAsync() |> Async.AwaitTask
                use reader = new StreamReader(content)
                let html = reader.ReadToEnd()
                return Ok html
            with ex ->
                return Error ex.Message
    }

type EnetBody = { d: string }

let enet (url: string) : Async<Result<string, string>> =
    async {
        let nid = Regex.Match(url, patternEnet).Groups.[1].Value

        let obj =
            $"""{{ "codigoInstituicao": "2", "numeroProtocolo": "{nid}", "token": "", "versaoCaptcha": "" }}"""

        if String.IsNullOrEmpty(nid) then
            return Error("Invalid URL " + url)
        else
            let httpClient = new HttpClient()

            let! response =
                httpClient.PostAsync(
                    "https://www.rad.cvm.gov.br/ENET/frmExibirArquivoIPEExterno.aspx/ExibirPDF",
                    new StringContent(obj, System.Text.Encoding.UTF8, "application/json")
                )
                |> Async.AwaitTask

            let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask

            if response.IsSuccessStatusCode then
                match Decode.Auto.fromString<EnetBody> content with
                | Ok json -> return Ok json.d
                | Error err -> return Error("Error: " + err)
            else
                return Error("Error: " + content)
    }

let strcasestr (pattern: string) (bytes: byte[]) : bool =
    let body = System.Text.Encoding.UTF8.GetString(bytes)
    body.ToUpper().Contains(pattern.ToUpper())

let removeQuotes (body: string) : string =
    if body.StartsWith("\"") && body.EndsWith("\"") then
        body[1 .. (max 1 body.Length - 2)] // remove quotes from "body"
    else
        body

let writeToFile (filename: string) (data': string) : Async<string> =
    async {
        use fileStream = new FileStream(filename, FileMode.Create)
        let bytesUtf8 = System.Text.Encoding.UTF8.GetBytes data'
        do! fileStream.WriteAsync(bytesUtf8, 0, data'.Length) |> Async.AwaitTask
        return filename
    }

let save (folder: string) (filename: string) (body: string) : Async<Result<string, string>> =
    let ext (bytes: byte[]) : string =
        if strcasestr "<html>" bytes then ".html" else ".pdf"

    async {
        try
            let decoded = removeQuotes body |> Convert.FromBase64String
            System.IO.Directory.CreateDirectory(_outputDir +/ folder) |> ignore
            let filePath = _outputDir +/ folder +/ (filename + ext decoded)
            // Return error if files already exists
            if File.Exists filePath then
                return Error "arquivo já existe"
            else 
                use fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write)
                do! fileStream.WriteAsync(decoded, 0, decoded.Length) |> Async.AwaitTask
                return Ok filePath
        with ex ->
            return Error(sprintf "erro ao salvar: %s" ex.Message)
    }

let getBody (url: string) =
    let m =
        List.tryFind (fun pattern -> Regex.IsMatch(url, pattern)) [ patternFnet; patternEnet ]

    async {
        match m with
        | None -> return Error $"URL inválida '{url}'"
        | Some patternFound ->
            match patternFound with
            | _ when patternFound = patternFnet -> return! fnet url
            | _ when patternFound = patternEnet -> return! enet url
            | _ -> return Error $"erro na URL {url}"
    }

let downloadAndSaveFile folder filename url : Result<string, string> =
    async {
        let! bodyResult = getBody url

        match bodyResult with
        | Error err -> return Error err
        | Ok body -> return! save folder filename body
    }
    |> Async.RunSynchronously

let filesListFilename = _outputDir +/ "files.lst"

let saveFileList (filelist: List<string * string>) =
    let directory = Path.GetDirectoryName(filesListFilename)

    if not (Directory.Exists(directory)) then
        Directory.CreateDirectory(directory) |> ignore

    try
        let lines = filelist |> List.map (fun (ticker, filename) -> ticker + ";" + filename)

        let fileMode =
            if File.Exists(filesListFilename) then
                FileMode.Append
            else
                FileMode.Create
        use fileStream = new FileStream(filesListFilename, fileMode, FileAccess.Write)
        use streamWriter = new StreamWriter(fileStream)
        lines |> List.iter streamWriter.WriteLine
        streamWriter.Flush()
        fileStream.Flush(true)
    with err ->
        Log.error err.Message

let readFile (filePath: string) : Option<List<string * string>> =
    try
        if File.Exists(filePath) then
            File.ReadAllLines(filePath)
            |> Array.distinct
            |> Array.toList
            |> List.map (fun line -> line.Split(';'))
            |> List.map (fun arr -> (arr.[0], arr.[1]))
            |> Some
        else
            Log.warning "arquivo de listagem de arquivos não encontrado"
            None
    with err ->
        Log.error err.Message
        None

let readFileList () = readFile filesListFilename