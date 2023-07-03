module Details

open System
open System.Net.Http
open System.Text.RegularExpressions

open Lib
open News
open HaskellInterop

type NewsDetail =
    { ticker: string
      filename: string
      url: string }

let getDetails (newsID: string) (agency: string) (dt: string) =
    async {
        let url =
            "https://sistemasweb.b3.com.br/PlantaoNoticias/Noticias/Detail?idNoticia="
            + Uri.EscapeDataString(newsID)
            + "&agencia="
            + Uri.EscapeDataString(agency)
            + "&dataNoticia="
            + Uri.EscapeDataString(dt)

        use httpClient = new HttpClient()
        let! response = httpClient.GetAsync(url) |> Async.AwaitTask
        return response
    }

let getURL (resp: HttpResponseMessage) : string =
    let txt = resp.Content.ReadAsStringAsync().Result
    let regexPattern = "(http[s]*://[^\\?]+\\?[Ii][Dd]=[0-9]+)"
    let matchResult = Regex.Match(txt, regexPattern)
    matchResult.Value

let nwsMsgToNewsDetail (nwsmsg: News.NwsMsg) : Async<Result<NewsDetail, string>> =
    async {
        let nid = string (nwsmsg.id)
        let nidAgencia = string (nwsmsg.IdAgencia)
        let ndateTime = nwsmsg.dateTime
        let! details = getDetails nid nidAgencia ndateTime
        let status = details.StatusCode
        let code = int status
        let msg = status.ToString()

        if code < 200 || code >= 300 then
            return (Error("[" + code.ToString() + "] " + msg + ": " + string (nwsmsg.headline)))
        else
            match ndateTime + "-" + nwsmsg.headline |> safeFilename with
            | None -> return (Error "nome do arquivo não disponível")
            | Some filename -> 
                return Ok(
                    { ticker = nwsmsg.ticker |> Option.defaultValue ""
                      filename = filename
                      url = getURL details })
    }

let transform (f: 'a -> Async<Result<'b, string>>) (list: 'a list) : Result<'b list, string> =
    let rec sequenceResults acc =
        function
        | [] -> Ok(List.rev acc)
        | x :: xs ->
            match f x with
            | Success(Ok value) -> sequenceResults (value :: acc) xs
            | Success(Error errorMessage) -> Error errorMessage
            | Failure ex -> Error ex.Message

    sequenceResults [] list


let fetchNewsFiles (codes: string list) (d1: string) (d2: string) : Result<NewsDetail list, string> =
    let result = News.fetchAndFilterNews codes d1 d2

    match result with
    | News.Success nwsmsgs ->
        match nwsmsgs with
        | Ok nwsmsgs' -> transform nwsMsgToNewsDetail nwsmsgs'
        | Error errorMessage -> Error errorMessage
    | News.Failure ex -> Error ex.Message
