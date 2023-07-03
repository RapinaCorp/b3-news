open System
open System.Reflection

let exeName = Assembly.GetEntryAssembly().GetName().Name

let download (d: Details.NewsDetail) =
    match Download.downloadAndSaveFile d.ticker d.filename d.url with
    | Ok filename -> printfn "=> %s" filename
    | Error err ->   printfn "x  Erro: %s | %s" err d.filename

let run codes d1 d2 =
    match Details.fetchNewsDetails codes d1 d2 with
    | Ok details -> details |> List.iter (fun detail -> download detail) |> Ok
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
    
    let d2 = 
        if args.Length >= 2 then
            args.[1]
        else
            args.[0]

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
