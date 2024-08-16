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
    | Ok filename -> printfn "=> %s" filename; Some (d.ticker, filename)
    | Error err -> Log.error <| sprintf "x Erro (download): %s | %s" err d.filename; None

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

let waitForKeyPressAsync (cts: CancellationTokenSource) (refreshFun: char -> unit) indexFun = async {
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

let serverUsage () = "'0'-'9' - recarrega arquivos desde 'n' dias atrás\n'i' - recria index.html\n'q' - sai do servidor\n"

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
    if args.Length = 0 then usage ()

    match args.[0] with
    | "fix" -> Fix.reclassify (); 0
    | "index" -> createIndex ()
    | "server" -> runServer ()
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

    if not (isDateFormat d1) || not (isDateFormat d2) then usage ()
    let codes = 
        if args.Length > 1 then Array.skip 1 args |> Array.toList
        else Config.readTickersFromFile ()

    match run codes d1 d2 with
    | Ok _ -> createIndex ()
    | Error err -> Log.error <| sprintf "x Erro (updateFiles): %s" err; 1

and runServer () =
    let port = WebServer.findFreePort 8080
    if  port.IsNone then
        1
    else
        use cts = new CancellationTokenSource()
        let updateFiles' (key: char) = updateFiles (string key) [||] |> ignore
        let createIndex' () = createIndex () |> ignore
        let keyPress = waitForKeyPressAsync cts updateFiles' createIndex'
        let server = WebServer.serveStaticFilesAsync cts port.Value (serverUsage ())
        [server; keyPress] |> Async.Parallel |> Async.RunSynchronously |> ignore
        0
