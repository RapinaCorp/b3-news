module News

open System
open System.Net.Http
open System.Text.RegularExpressions

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

let fetchAllNews (d1: string) (d2: string) =
    async {
        let client = new HttpClient()
        let query = System.Web.HttpUtility.ParseQueryString("")
        query.Add("agencia", "18")
        query.Add("dataInicial", d1)
        query.Add("dataFinal", d2)

        let uriBuilder =
            UriBuilder("https://sistemasweb.b3.com.br/PlantaoNoticias/Noticias/ListarTitulosNoticias")

        uriBuilder.Query <- query.ToString()

        let! response = client.GetAsync(uriBuilder.Uri) |> Async.AwaitTask
        response.EnsureSuccessStatusCode() |> ignore

        let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask

        return Decode.Auto.fromString<NwsMsgWrapper list> content
    }

let getTicker (txt: string) =
    if txt = null then
        ""
    else
        let regex = new Regex("\\((....).*\\)")
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
                Some { nwsmsg with ticker = Some ticker } // Add ticker to nwsmsg
            else
                None)
        nwsmsgs
    |> List.sortBy (fun nwsmsg -> nwsmsg.ticker)

let fetchAndFilterNews (tickers: string list) (d1: string) (d2: string) =
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
            printfn "- %d fatos relevantes no período" nwsMsgs.Length
            printfn "- %d itens filtrados" filteredNews.Length
            return Ok(filteredNews)
        | Error errorMessage -> return Error errorMessage
    }
