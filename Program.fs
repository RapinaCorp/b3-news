open System
open System.Text.RegularExpressions

let isDateFormat (input: string) =
    let pattern = @"^\d{4}-\d{2}-\d{2}$"
    Regex.IsMatch(input, pattern)

let usage () =
    let exeName = System.Reflection.Assembly.GetEntryAssembly().GetName().Name
    printfn "Uso: %s <comando> [opções]" exeName
    printfn ""
    printfn "Comandos disponíveis:"
    printfn "  update <data1> [<data2>]        Atualiza relatórios JSON"
    printfn "  server [opções]                 Inicia o servidor web"
    printfn ""
    printfn "  help                            Mostra esta mensagem de ajuda"
    printfn ""
    printfn "Formato da data: AAAA-MM-DD"
    printfn ""
    exit 0

let updateReportsJson (args: string array) =
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
        Log.error <| sprintf "x Erro (updateReportsJson): %s" err
        1

[<EntryPoint>]
let main (args: string array) : int =
    if args.Length = 0 || args.[0] = "help" then
        usage ()

    match args.[0] with
    | "server" -> Api.startApiServer args[1..]
    | "update" -> updateReportsJson args[1..]
    | _ -> usage ()
