open System
open System.IO
open System.Reflection
open System.Threading
open System.Text.RegularExpressions

let isDateFormat (input: string) =
    let pattern = @"^\d{4}-\d{2}-\d{2}$"
    Regex.IsMatch(input, pattern)

let download folderName (d: Details.NewsDetail) : Async<Option<string * string>> =
    async {
        let! result = Download.downloadAndSaveFileAsync (folderName d.filename) d.filename d.url
        match result with
        | Ok (Download.Written path) ->
            printfn "=> %s" path
            return Some(d.ticker, path)
        | Ok (Download.Skipped path) ->
            Log.notice <| sprintf "=> %s (arquivo já existe)" path
            return None
        | Error err ->
            Log.error <| sprintf "x Erro (download): %A | %s" err d.filename
            return None
    }

let run codes d1 d2 =
    async {
        let pe = Predicitions.setPrediction
        let folderName = Predicitions.predictFolder pe

        Log.notice <| sprintf "- Verificando fatos relevantes entre %s e %s:" d1 d2
        Log.notice <| sprintf "  %A\n" codes

        let! detailsResult = Details.fetchNewsDetailsAsync codes d1 d2
        match detailsResult with
        | Error e -> return Error e
        | Ok details ->
            let! downloadResults =
                details
                |> List.map (download folderName)
                |> Async.Parallel
            let successfulDownloads =
                downloadResults
                |> Array.choose id
                |> Array.toList
            Download.saveFileList successfulDownloads
            return Ok ()
    }

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
    async {
        let HtmlEncode (s: string) = System.Web.HttpUtility.HtmlEncode(s)

        use stream =
            Assembly
                .GetExecutingAssembly()
                .GetManifestResourceStream("B3News.reports_template.html")
        use reader = new StreamReader(stream)
        let template = reader.ReadToEnd()

        let! detailsResult = Details.fetchNewsDetailsAsync codes d1 d2
        return
            detailsResult
            |> Result.map (fun details ->
                let htmlRows =
                    details
                    |> List.sortByDescending (fun d -> d.dateTime)
                    |> List.map (fun d ->
                        match List.tryParseReportFromFilename d.filename with
                        | Some report ->
                            let aTag = Download.getHtmlLink d.url d.filename
                            let bTag = Download.getHtmlLink d.url <| HtmlEncode(report.name.Replace('_', ' '))
                            $"<tr><td><b>{HtmlEncode(d.ticker)}</b></td><td>{bTag}</td><td>{HtmlEncode(d.dateTime.Substring(0, 16))}</td><td>{aTag}</td></tr>"
                        | None ->
                            let aTag = Download.getHtmlLink d.url d.filename
                            $"<tr><td colspan='3'>{HtmlEncode(d.filename)}</td><td>{aTag}</td></tr>")
                    |> String.concat "\n"
                let html = template.Replace("{rows}", htmlRows)
                let outFile = "reports.html"
                File.WriteAllText(outFile, html)
                Log.notice $"Relatório salvo em {outFile}"
                outFile)
    }

let serverUsage () =
    "'0'-'9' - recarrega arquivos desde 'n' dias atrás\n'i' - recria index.html\n'q' - sai do servidor\n"

let usage () =
    let exeName = Assembly.GetEntryAssembly().GetName().Name
    printfn "Uso: %s <comando> [opções]" exeName
    printfn ""
    printfn "Comandos disponíveis:"
    printfn "  serve                                             Inicia o servidor web (em primeiro plano)"
    printfn "  daemon                                            Inicia o servidor web em segundo plano"
    printfn "  build-index                                       Recria o arquivo index.html"
    printfn "  generate-report <data1> [<data2>] [<códigos>...]  Gera relatorios.html"
    printfn "  update-json <data1> [<data2>] [<códigos>...]      Atualiza relatórios JSON"
    printfn "  reclassify                                        Move arquivos após reclassificação de dados"
    printfn "  list-resources                                    Lista os recursos disponíveis"
    printfn "  serve-api [opções]                                Inicia o servidor de API JSON"
    printfn ""
    printfn "  <data1> [<data2>] [<códigos>...]                  Atualiza arquivos (padrão)"
    printfn ""
    printfn "  help                                              Mostra esta mensagem de ajuda"
    printfn ""
    printfn "Formato da data: AAAA-MM-DD (ou 'hoje')"
    printfn "Códigos: Códigos das empresas (4 caracteres) ou lidos de report/b3news.cfg se não fornecidos."
    printfn ""
    Environment.Exit 0

[<EntryPoint>]
let rec main (args: string array) : int =
    if args.Length = 0 || args.[0] = "help" then
        usage ()

    match args.[0] with
    | "reclassify" ->
        Fix.reclassify ()
        0
    | "build-index" -> createIndex ()
    | "serve" -> runServer ()
    | "daemon" -> runDaemon ()
    | "generate-report" -> reportsFile args[1..]
    | "list-resources" ->
        Predicitions.listResources ()
        0
    | "serve-api" -> Api.startApiServer args[1..]
    | "update-json" -> updateJson args[1..]
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

    run codes d1 d2
    |> Async.RunSynchronously
    |> function
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
    listReports codes d1 d2
    |> Async.RunSynchronously
    |> function
        | Ok _ -> 0
        | Error err ->
            Log.error <| sprintf "x Erro (list): %s" err
            1

and updateJson (args: string array) =
    let d1 =
        if args.Length >= 1 then
            args.[0]
        else
            DateTime.Today.ToString("yyyy-MM-dd")
    let d2 =
        if args.Length >= 2 then
            args.[1]
        else
            DateTime.Today.ToString("yyyy-MM-dd")
    if not (isDateFormat d1) || not (isDateFormat d2) then
        usage ()
    let codes = Config.readTickersFromFile ()
    printfn "- Atualizando relatórios entre %s e %s:" d1 d2
    printfn "  %A\n" codes

    let updateTask = JsonStore.updateNewsCust Config.reportsJson codes d1 d2
    match Async.RunSynchronously updateTask with
    | Ok num ->
        Log.notice <| sprintf "- %d relatórios adicionados." num
        0
    | Error err ->
        Log.error <| sprintf "x Erro (updateJson): %s" err
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

and runDaemon () =
    let port = WebServer.findFreePort 8080
    if port.IsNone then
        1
    else
        use cts = new CancellationTokenSource()
        let server = WebServer.serveStaticFilesAsync cts port.Value (serverUsage ())
        server |> Async.RunSynchronously |> ignore
        0
