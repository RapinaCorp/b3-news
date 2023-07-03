open System
open System.Reflection

let exeName = Assembly.GetEntryAssembly().GetName().Name

let download ticker filename url =
    match Download.downloadAndSaveFile ticker filename url with
    | Ok filename -> printfn "=> %s" filename
    | Error err ->   printfn "x  Erro: %s | %s" err filename

let run codes d1 d2 =
    match Details.fetchNewsFiles codes d1 d2 with
    | Ok urls -> urls |> List.iter (fun nf -> download nf.ticker nf.filename nf.url) |> Ok
    | Error e -> Error e

[<EntryPoint>]
let main args =
    if args.Length < 3 then
        printfn "Usage: %s AAAA-MM-DD AAAA-MM-DD COD1 COD2 COD3..." exeName
        Environment.Exit 0

    let d1 = args.[0]
    let d2 = args.[1]
    let codes = Array.skip 2 args |> Array.toList

    printfn $"- Verificando fatos relevantes entre {d1} e {d2}:"
    printfn "  %A\n" codes

    match run codes d1 d2 with
    | Ok _ -> 0
    | Error err -> 
        printfn "x  Erro: %s" err
        1
