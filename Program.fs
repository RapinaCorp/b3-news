open System
open System.IO
open System.Reflection
open System.Threading
open System.Text.RegularExpressions

let isDateFormat (input: string) =
    let pattern = @"^\d{4}-\d{2}-\d{2}$"
    Regex.IsMatch(input, pattern)

let download folderName (d: Details.NewsDetail) : Option<string * string> =
    match Download.downloadAndSaveFile (folderName d.filename) d.filename d.url with
    | Ok filename ->
        printfn "=> %s" filename
        Some(d.ticker, filename)
    | Error err ->
        Log.error <| sprintf "x Erro (download): %s | %s" err d.filename
        None

let run codes d1 d2 =
    let pe = Predicitions.setPrediction
    let folderName = Predicitions.predictFolder pe

    Log.notice <| sprintf "- Verificando fatos relevantes entre %s e %s:" d1 d2
    Log.notice <| sprintf "  %A\n" codes

    match Details.fetchNewsDetails codes d1 d2 with
    | Ok details ->
        details
        |> List.map (download folderName)
        |> List.choose id
        |> Download.saveFileList
        |> Ok
    | Error e -> Error e

let waitForKeyPressAsync (cts: CancellationTokenSource) (refreshFun: char -> unit) indexFun =
    async {
        while not cts.Token.IsCancellationRequested do
            if Console.KeyAvailable then
                let key = Console.ReadKey(intercept = true)
                if key.KeyChar = 'q' then
                    Log.warning "\n- 'q': Parando o servidor..."
                    cts.Cancel()
                if key.KeyChar >= '0' && key.KeyChar <= '9' then
                    Log.notice $"\n- '{key.KeyChar}': atualizando arquivos... "
                    refreshFun key.KeyChar
                if key.KeyChar = 'i' then
                    Log.notice "\n- 'i': recriando index.html..."
                    indexFun ()

            do! Async.Sleep 500
    }

let listReports codes d1 d2 =
    let HtmlEncode (s: string) = System.Web.HttpUtility.HtmlEncode(s)
    match Details.fetchNewsDetails codes d1 d2 with
    | Ok details ->
        let htmlRows =
            details
            |> List.sortByDescending (fun d -> d.dateTime)
            |> List.map (fun d ->
                match List.tryParseReportFromFilename d.filename with
                | Some report ->
                    let aTag = Download.getHtmlLink d.url d.filename
                    $"<tr><td><b>{HtmlEncode(d.ticker)}</b></td><td>{HtmlEncode(report.name.Replace('_', ' '))}</td><td>{HtmlEncode(d.dateTime.Substring(0, 16))}</td><td>{aTag}</td></tr>"
                | None ->
                    let aTag = Download.getHtmlLink d.url d.filename
                    $"<tr><td colspan='3'>{HtmlEncode(d.filename)}</td><td>{aTag}</td></tr>")
            |> String.concat "\n"
        let html =
            $"""
            <html>
            <head>
                <meta charset="utf-8"/>
                <title>Relatórios</title>
                <style>table{{border-collapse:collapse;margin:auto;font:0.75rem Consolas,monospace;min-width:400px;box-shadow:0 0 20px rgba(0,0,0,.15)}}thead tr{{background:#003475;color:#fff;text-align:left;font-size:.9rem}}th{{padding:12px 15px}}td{{padding:6px}}tbody tr{{border-bottom:1px solid #ddd}}tbody tr:nth-of-type(2n){{background:#f3f3f3}}tbody tr:last-of-type{{border-bottom:2px solid #003475}}tbody tr:hover{{background:#ff9}}td:first-child{{text-align:center}}</style>
            </head>
            <body>
                <h2>Relatórios encontrados</h2>
                <table>
                    <thead><tr><th>Empresa</th><th>Nome</th><th>Data</th><th>URL</th></tr></thead>
                    <tbody>{htmlRows}</tbody>
                </table>
            </body>
            </html>
            """
        let outFile = "reports.html"
        File.WriteAllText(outFile, html)
        Log.notice $"Relatório salvo em {outFile}"
        Ok outFile
    | Error e -> Error e

let serverUsage () =
    "'0'-'9' - recarrega arquivos desde 'n' dias atrás\n'i' - recria index.html\n'q' - sai do servidor\n"

let usage () =
    let exeName = Assembly.GetEntryAssembly().GetName().Name
    printfn "Uso: %s <data1> [<data2>] [<códigos>...]" exeName
    printfn "     %s server - inicia servidor web" exeName
    printfn "     %s index  - recria arquivo index.html" exeName
    // printfn "     %s fix    - move arquivos após reclassificação de dados " exeName
    printfn "Formato da data: AAAA-MM-DD (ou 'hoje')"
    printfn "Códigos: Códigos das empresas ou lidos de report/b3news.cfg se não fornecidos."
    printfn "         Usar códigos com 4 caracteres."
    Environment.Exit 0

[<EntryPoint>]
let rec main (args: string array) : int =
    if args.Length = 0 then
        usage ()

    System.Net.ServicePointManager.SecurityProtocol <-
        System.Net.SecurityProtocolType.Tls12 ||| System.Net.SecurityProtocolType.Tls13

    match args.[0] with
    | "fix" ->
        Fix.reclassify ()
        0
    | "index" -> createIndex ()
    | "server" -> runServer ()
    | "list" -> reportsFile args[1..]
    | command -> updateFiles command args[1..]

and createIndex () =
    match Download.readFileList () with
    | Some filelist ->
        List.createIndexHtml filelist
        Log.notice "- index.html criado."
    | None -> Log.error "Não foi possível carregar o arquivo de índice."
    0

and updateFiles command args =
    let mutable num = 0
    let isNum = Int32.TryParse(command, &num)

    let d1 =
        match command with
        | "hoje" -> DateTime.Today.ToString("yyyy-MM-dd")
        | _ when isNum -> DateTime.Today.AddDays(-num).ToString("yyyy-MM-dd")
        | _ -> command

    let d2 =
        match args.Length with
        | len when len > 0 -> args.[0]
        | _ when isNum -> DateTime.Today.ToString("yyyy-MM-dd")
        | _ -> d1

    if not (isDateFormat d1) || not (isDateFormat d2) then
        usage ()
    let codes =
        if args.Length > 1 then
            Array.skip 1 args |> Array.toList
        else
            Config.readTickersFromFile ()

    match run codes d1 d2 with
    | Ok _ -> createIndex ()
    | Error err ->
        Log.error <| sprintf "x Erro (updateFiles): %s" err
        1

and reportsFile (args: string array) =
    if args.Length < 3 then
        usage ()
    let d1 = args.[0]
    let d2 =
        if args.Length > 2 then
            args.[1]
        else
            DateTime.Today.ToString("yyyy-MM-dd")
    if not (isDateFormat d1) || not (isDateFormat d2) then
        usage ()
    let codes = Array.skip 2 args |> Array.toList
    printfn "- Listando relatórios entre %s e %s:" d1 d2
    printfn "  %A\n" codes
    match listReports codes d1 d2 with
    | Ok _ -> 0
    | Error err ->
        Log.error <| sprintf "x Erro (list): %s" err
        1

and runServer () =
    let port = WebServer.findFreePort 8080
    if port.IsNone then
        1
    else
        use cts = new CancellationTokenSource()
        let updateFiles' (key: char) = updateFiles (string key) [||] |> ignore
        let createIndex' () = createIndex () |> ignore
        let keyPress = waitForKeyPressAsync cts updateFiles' createIndex'
        let server = WebServer.serveStaticFilesAsync cts port.Value (serverUsage ())
        [ server; keyPress ] |> Async.Parallel |> Async.RunSynchronously |> ignore
        0
