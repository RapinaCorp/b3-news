open System
open System.IO
open System.Reflection
open System.Threading

let download folderName (d: Details.NewsDetail) : Option<string * string> =
    match Download.downloadAndSaveFile (folderName d.filename) d.filename d.url with
    | Ok filename -> printfn "=> %s" filename; Some (d.ticker, filename)
    | Error err -> printfn "x  Erro: %s | %s" err d.filename; None

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

let waitForKeyPressAsync (cts: CancellationTokenSource) r = async {
    while not cts.Token.IsCancellationRequested do
        if Console.KeyAvailable then
            let key = Console.ReadKey(intercept = true)
            if key.KeyChar = 'q' then
                Log.warning "- 'q': Parando o servidor..."
                cts.Cancel()
            if key.KeyChar = 'r' then
                Log.notice "- 'r': atualizando arquivos... "
                r ()

        do! Async.Sleep 500 
}

[<EntryPoint>] 
let rec main (args: string array) : int =
    let exeName = Assembly.GetEntryAssembly().GetName().Name
    let usage () =
        printfn "Uso: %s <data1> [<data2>] [<códigos>...]" exeName
        printfn "     %s server - inicia servidor web" exeName
        printfn "     %s index  - recria arquivo index.html" exeName
        // printfn "     %s fix    - move arquivos após reclassificação de dados " exeName
        printfn "Formato da data: AAAA-MM-DD (ou 'hoje')"
        printfn "Códigos: Códigos das empresas ou lidos de report/b3news.cfg se não fornecidos."
        printfn "         Usar códigos com 4 caracteres."
        Environment.Exit 0

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
    let d1 = if command = "hoje" then DateTime.Today.ToString("yyyy-MM-dd") else command
    let d2 = if args.Length > 0 then args.[0] else d1
    let codes = 
        if args.Length > 1 then Array.skip 1 args |> Array.toList
        else Config.readTickersFromFile ()

    match run codes d1 d2 with
    | Ok _ -> createIndex ()
    | Error err -> sprintf "x  Erro: %s" err |> Log.error; 1

and runServer () =
    use cts = new CancellationTokenSource()
    let server = WebServer.serveStaticFilesAsync 8080 cts 
    let keyPress = waitForKeyPressAsync cts (fun () -> updateFiles "hoje" [||] |> ignore)
    [server; keyPress] |> Async.Parallel |> Async.RunSynchronously |> ignore
    Log.notice "Servidor parado."
    0


