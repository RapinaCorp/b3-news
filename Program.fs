open System

/// Validates and normalizes a CLI date argument. `DateTime.TryParseExact`
/// instead of a regex: the old `^\d{4}-\d{2}-\d{2}$` check let impossible
/// dates (`2026-13-99`) through, which later crashed `News.parseDate`
/// (`DateTime.ParseExact`) with an uncaught FormatException.
let parseDateFormat (input: string) : DateTime option =
    match
        DateTime.TryParseExact(
            input,
            "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None
        )
    with
    | true, d -> Some d
    | _ -> None

let usage (exitCode: int) =
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
    exit exitCode

let updateReportsJson (args: string array) =
    let d1 =
        if args.Length >= 1 then
            args.[0]
        else
            News.todayB3().ToString("yyyy-MM-dd")
    let d2 =
        if args.Length >= 2 then
            args.[1]
        else
            d1

    match parseDateFormat d1, parseDateFormat d2 with
    | Some d1', Some d2' ->
        // Normalize both dates: only what survived validation reaches
        // News.parseDate, so it can no longer throw on garbage input.
        let d1 = d1'.ToString("yyyy-MM-dd")
        let d2 = d2'.ToString("yyyy-MM-dd")
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
    | _ ->
        Log.error "Data inválida — use o formato AAAA-MM-DD (ex: 2026-09-17)."
        1

[<EntryPoint>]
let main (args: string array) : int =
    if args.Length = 0 || args.[0] = "help" then
        usage 0

    match args.[0] with
    | "server" -> Api.startApiServer args[1..]
    | "update" -> updateReportsJson args[1..]
    | _ -> usage 1
