open System
open System.IO
open System.Reflection

let (+/) path1 path2 = Path.Combine(path1, path2)
let exeName = Assembly.GetEntryAssembly().GetName().Name

let download folderName (d: Details.NewsDetail) : Option<string * string> =
    match Download.downloadAndSaveFile (folderName d.filename) d.filename d.url with
    | Ok filename -> printfn "=> %s" filename; Some (d.ticker, filename)
    | Error err -> printfn "x  Erro: %s | %s" err d.filename; None

let run codes d1 d2 =
    let pe = Predicitions.setPrediction
    let folderName = Predicitions.predictFolder pe

    match Details.fetchNewsDetails codes d1 d2 with
    | Ok details ->
        details 
        |> List.map (download folderName) 
        |> List.choose id 
        |> List.createIndexHtml
        |> Ok
    | Error e -> Error e

[<EntryPoint>]
let main args =
    if args.Length < 1 then
        printfn "Uso: %s AAAA-MM-DD AAAA-MM-DD CODA CODB CODC..." exeName
        printfn "Uso: ou %s AAAA-MM-DD AAAA-MM-DD" exeName
        printfn "     e os códigos das empresas em b3news.cfg, um código por linha,"
        printfn "     sem os números. Ex.: PETR em vez de PETR4.\n"
        Environment.Exit 0

    let d1 = args.[0]

    if d1 = "fix" then
        Fix.reclassify ()
        Environment.Exit 0

    if d1 = "index" then
        let d2 = if args.Length >= 2 then args.[1] else "."
        List.printHtml d2
        Environment.Exit 0

    let d2 = if args.Length >= 2 then args.[1] else args.[0]

    let codes =
        if args.Length >= 3 then
            Array.skip 2 args |> Array.toList
        else
            Config.readTickersFromFile

    sprintf $"- Verificando fatos relevantes entre {d1} e {d2}:" |> Log.notice
    sprintf "  %A\n" codes |> Log.notice

    let r = run codes d1 d2
    printfn ""

    match r with
    | Ok _ -> 0
    | Error err ->
        sprintf "x  Erro: %s" err |> Log.error
        1
