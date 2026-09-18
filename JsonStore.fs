module JsonStore

open System.Text.Json
open System.IO
open DetailsQuery

let private options = JsonSerializerOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)

let tryRead path : Result<Details.NewsDetail list, string> =
    if File.Exists path then
        try File.ReadAllText path |> fun j -> JsonSerializer.Deserialize<Details.NewsDetail list>(j, options) |> Ok
        with :? JsonException as e -> Error $"JSON deserialization error: {e.Message}"
             | e -> Error $"Error reading/deserializing JSON: {e.Message}"
    else Ok []

let private tryWrite path details =
    try
        // Atomic replace: write to a sibling temp file, then rename over the
        // target. A crash mid-write leaves either the old file or the new one,
        // never a truncated hybrid (same directory ⇒ same volume ⇒ rename is
        // atomic on NTFS and POSIX).
        let json = JsonSerializer.Serialize(details, options)
        let tmp = path + ".tmp"
        File.WriteAllText(tmp, json)
        File.Move(tmp, path, true)
        Ok()
    with e -> Error $"Error writing JSON: {e.Message}"

let private writeLock = obj ()

/// Merges freshly fetched details into the existing list: preserves cached
/// summaries for re-fetched items, dedupes by (ticker, dateTime, filename,
/// url) and sorts newest-first. Returns (addedCount, merged).
///
/// Pure (no I/O) so legacy data shapes — e.g. duplicate keys in `existing`,
/// which older merges or hand edits can produce — are testable without
/// network. `addedCount` is the number of distinct new keys absent from
/// `existing`, so duplicates in either list can't skew it.
let mergeDetails
    (existing: Details.NewsDetail list)
    (newDetails: Details.NewsDetail list)
    : int * Details.NewsDetail list =
    let key (x: Details.NewsDetail) = x.ticker, x.dateTime, x.filename, x.url

    // Map.ofList throws on duplicate keys; last one wins via Map.Add.
    let existingByKey =
        existing
        |> List.fold (fun (m: Map<_, Details.NewsDetail>) d -> m.Add(key d, d)) Map.empty

    let newDetailsPreserved =
        newDetails
        |> List.map (fun nd ->
            match Map.tryFind (key nd) existingByKey with
            | Some old when not (System.String.IsNullOrWhiteSpace old.summary) ->
                { nd with summary = old.summary }
            | _ -> nd)

    let addedCount =
        newDetailsPreserved
        |> List.map key
        |> List.distinct
        |> List.filter (fun k -> not (existingByKey.ContainsKey k))
        |> List.length

    let merged =
        newDetailsPreserved @ existing
        |> List.distinctBy key
        |> List.sortByDescending (_.dateTime)

    addedCount, merged

let updateNewsCust jsonPath codes startDate endDate : Async<Result<int, string>> =
    async {
        let! detailsResult = Details.fetchNewsDetailsAsync codes startDate endDate
        return
            detailsResult
            |> Result.bind (fun newDetails ->
                let newDetailsTransformed =
                    newDetails |> List.map (fun nd -> { nd with url = Download.getRealUrl nd.url })
                tryRead jsonPath
                |> Result.bind (fun existing ->
                    let addedCount, merged = mergeDetails existing newDetailsTransformed
                    tryWrite jsonPath merged
                    |> Result.map (fun () -> addedCount)))
    }

/// Atualiza o campo `summary` do item com o `id` informado, relendo o
/// arquivo na hora (não usa uma cópia antiga carregada antes) para minimizar
/// o risco de sobrescrever mudanças feitas por outro processo (ex: um
/// `update-json` rodando ao mesmo tempo). Serializa escritas concorrentes
/// dentro do próprio processo com `writeLock`.
let updateSummary (jsonPath: string) (id: int) (summary: string) : Result<unit, string> =
    lock writeLock (fun () ->
        tryRead jsonPath
        |> Result.bind (fun details ->
            let found = details |> List.exists (fun d -> d.id = id)
            if not found then
                Error(sprintf "Item com id=%d não encontrado em %s." id jsonPath)
            else
                details
                |> List.map (fun d -> if d.id = id then { d with summary = summary } else d)
                |> tryWrite jsonPath))

let updateNews codes startDate endDate : Async<Result<int,string>> =
    updateNewsCust Config.reportsJson codes startDate endDate

let loadPagedNewsWithCriteria jsonPath (criteria: NewsFilterCriteria) : Result<PagedResult<Details.NewsDetail>, string> =
    tryRead jsonPath
    |> Result.bind (fun details ->
        if details.IsEmpty then
            Error "No news details found in the JSON file."
        else
            Ok <| DetailsQuery.filterAll criteria details)

let getNewsCount () : Result<int, string> =
    tryRead Config.reportsJson
    |> Result.map List.length