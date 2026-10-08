module News

open System
open System.Net.Http
open System.Text.RegularExpressions
open System.IO
open System.Security.Cryptography
open System.Text
open System.Globalization

open Thoth.Json.Net

type NwsMsg =
    { IdAgencia: int
      dateTime: string
      headline: string
      id: int
      ticker: string option }

type NwsMsgWrapper = { NwsMsg: NwsMsg }

let (|Success|Failure|) (result: Async<'T>) =
    try
        Success(Async.RunSynchronously result)
    with ex ->
        Failure ex

let cacheDir = "news_cache"

let getCacheFileName d1 d2 =
    let key = d1 + "_" + d2
    let hash =
        use sha = SHA256.Create()
        let bytes = Encoding.UTF8.GetBytes(key)
        sha.ComputeHash(bytes)
        |> Array.map (fun b -> b.ToString("x2"))
        |> String.concat ""
    Path.Combine(cacheDir, hash + ".json")

let parseDate (s: string) =
    DateTime.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture)

let private b3TimeZone =
    try TimeZoneInfo.FindSystemTimeZoneById "America/Sao_Paulo"
    with :? TimeZoneNotFoundException ->
        TimeZoneInfo.FindSystemTimeZoneById "E. South America Standard Time"

/// Data de hoje no fuso da B3.
let todayB3 () =
    TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, b3TimeZone).Date

/// Só intervalos encerrados antes de hoje são imutáveis e podem ir para o
/// cache. Hoje ou datas futuras ainda podem receber notícias.
let isCacheable (today: DateTime) (endDate: DateTime) =
    endDate.Date < today.Date

/// Module-level client (same lifecycle pattern as Download/OpenAIClient/
/// ContentExtractor): avoids per-call socket churn.
let private httpClient = new HttpClient()

/// Parses the cached JSON body into the news list. Pure, so the cache
/// decision (serve it, or delete and refetch) is testable without network.
let tryParseCache (cached: string) : Result<NwsMsgWrapper list, string> =
    Decode.Auto.fromString<NwsMsgWrapper list> cached

let fetchAllNews (d1: string) (d2: string) =
    async {
        let startDate = parseDate d1
        let endDate = parseDate d2
        if (endDate - startDate).TotalDays > 93.0 then // 3 months (approx)
            return Error "O intervalo máximo permitido é de 3 meses."
        else
            let cacheable = isCacheable (todayB3 ())  endDate
            let cacheFile = getCacheFileName d1 d2

            let fetchFresh () =
                async {
                    let query = System.Web.HttpUtility.ParseQueryString("")
                    query.Add("agencia", "18")
                    query.Add("dataInicial", d1)
                    query.Add("dataFinal", d2)
                    let uriBuilder =
                        UriBuilder("https://sistemasweb.b3.com.br/PlantaoNoticias/Noticias/ListarTitulosNoticias")
                    uriBuilder.Query <- query.ToString()
                    try
                        let! response = httpClient.GetAsync(uriBuilder.Uri) |> Async.AwaitTask
                        response.EnsureSuccessStatusCode() |> ignore
                        let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                        match tryParseCache content with
                        | Ok data ->
                            if cacheable then
                                Directory.CreateDirectory(cacheDir) |> ignore
                                do! File.WriteAllTextAsync(cacheFile, content) |> Async.AwaitTask
                            return Ok data
                        | Error err -> return Error <| sprintf "Erro ao listar notícias: %s" err
                    with ex ->
                        return Error(sprintf "HTTP error: %s" ex.Message)
                }

            if not cacheable then
                // Hoje ou futuro: nunca lê nem grava cache. Apaga eventual
                // sobra de versões anteriores, que gravavam o dia incompleto.
                if File.Exists cacheFile then File.Delete cacheFile
                return! fetchFresh ()
            elif File.Exists cacheFile then
                let! cached = File.ReadAllTextAsync(cacheFile) |> Async.AwaitTask
                match tryParseCache cached with
                | Ok data -> return Ok data
                | Error _ ->
                    // Corrupted cache: discard and fall through to a fresh fetch.
                    File.Delete(cacheFile)
                    return! fetchFresh ()
            else
                return! fetchFresh ()
    }

let getTicker (txt: string) =
    if txt = null then
        ""
    else
        let regex = new Regex("\\((....).*\\)") // Matches ticker in parentheses: "(ABCD)"
        let matchResult = regex.Match(txt)

        if matchResult.Success then
            matchResult.Groups.[1].Value
        else
            ""

let filterNews (tickers: string list) (nwsmsgs: NwsMsg list) =
    List.choose
        (fun nwsmsg ->
            let ticker = getTicker nwsmsg.headline
            if tickers |> List.exists (fun ticker' -> ticker = ticker'.ToUpper()) then
                Some { nwsmsg with ticker = Some ticker } // Add ticker code to nwsmsg
            else
                None)
        nwsmsgs
    |> List.sortBy (fun nwsmsg -> nwsmsg.ticker)

let fetchAndFilterNews (tickers: string list) (d1: string) (d2: string) : Async<Result<list<NwsMsg>, string>> =
    async {
        let d1', d2' =
            match d1, d2 with
            | "", "" -> "", ""
            | "", _ -> d2, d2
            | _, "" -> d1, d1
            | _, _ -> d1, d2

        let! result = fetchAllNews d1' d2'

        match result with
        | Ok nwsMsgs ->
            let filteredNews = List.map (fun el -> el.NwsMsg) nwsMsgs |> filterNews tickers
            sprintf "- %d fatos relevantes no período" nwsMsgs.Length |> Log.info
            sprintf "- %d itens filtrados\n" filteredNews.Length |> Log.info
            return Ok(filteredNews)
        | Error errorMessage -> return Error errorMessage
    }
