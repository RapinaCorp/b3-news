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
    printfn "  update-json <data1> [<data2>]   Atualiza relatórios JSON"
    printfn "  summarize                       Gera a página de resumos via NVIDIA"
    printfn "  serve-api [opções]              Inicia o servidor de API JSON"
    printfn ""
    printfn "  help                            Mostra esta mensagem de ajuda"
    printfn ""
    printfn "Formato da data: AAAA-MM-DD (ou 'hoje')"
    printfn ""
    exit 0

let updateJson (args: string array) =
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

let summarize (_args: string array) =
    match Config.nvidiaApiKey () with
    | Error err ->
        Log.error <| sprintf "x %s" err
        1
    | Ok apiKey ->
        let settings = Config.readSummarySettings ()
        let tickers = Config.readTickersFromFile ()
        if List.isEmpty tickers then
            Log.error "x Nenhum ticker em b3news.settings.json (Tickers)."
            1
        else
            match JsonStore.tryRead Config.reportsJson with
            | Error err ->
                Log.error <| sprintf "x Erro ao ler %s: %s" Config.reportsJson err
                1
            | Ok [] ->
                Log.error <| sprintf "x %s está vazio. Rode update-json antes." Config.reportsJson
                1
            | Ok details ->
                printfn "- Resumindo últimos %d comunicados de:" settings.MaxDocuments
                printfn "  %A" tickers
                printfn "  modelo=%s paralelismo=%d maxChars=%d" settings.Model settings.MaxParallelism settings.MaxChars
                let task =
                    SummaryPipeline.run
                        apiKey
                        settings.Model
                        settings.MaxRetries
                        settings.MaxChars
                        settings.MaxParallelism
                        details
                        tickers
                        settings.MaxDocuments
                        settings.DisableReasoning
                let ativos = Async.RunSynchronously task
                let path = SummaryPage.writeToFile settings.OutputFile DateTime.Now ativos
                let okCount = ativos |> List.sumBy (fun a -> a.Comunicados.Length)
                let failCount = ativos |> List.sumBy (fun a -> a.Falhas)
                Log.notice
                <| sprintf "- Página gerada: %s (%d resumos, %d falhas, %d ativos)." path okCount failCount ativos.Length
                0

[<EntryPoint>]
let main (args: string array) : int =
    if args.Length = 0 || args.[0] = "help" then
        usage ()

    match args.[0] with
    | "serve-api" -> Api.startApiServer args[1..]
    | "update-json" -> updateJson args[1..]
    | "summarize" -> summarize args[1..]
    | _ -> usage ()
