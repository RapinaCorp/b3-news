module JsonStore

open System.Text.Json
open System.IO
open DetailsQuery

let private options = JsonSerializerOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)

let private tryRead path : Result<Details.NewsDetail list, string> =
    if File.Exists path then
        try File.ReadAllText path |> fun j -> JsonSerializer.Deserialize<Details.NewsDetail list>(j, options) |> Ok
        with :? JsonException as e -> Error $"JSON deserialization error: {e.Message}"
             | e -> Error $"Error reading/deserializing JSON: {e.Message}"
    else Ok []

let private tryWrite path details =
    try
        JsonSerializer.Serialize(details, options)
        |> fun json -> File.WriteAllText(path, json)
        Ok()
    with e -> Error $"Error writing JSON: {e.Message}"

let updateNewsCust jsonPath codes startDate endDate : Result<int, string> =
    let key (x: Details.NewsDetail) = x.ticker, x.dateTime, x.filename, x.url

    Details.fetchNewsDetails codes startDate endDate
    |> Result.bind (fun newDetails ->
        let newDetailsTransformed =
            newDetails |> List.map (fun nd -> { nd with url = Download.getRealUrl nd.url })
        tryRead jsonPath
        |> Result.bind (fun existing ->
            let merged =
                newDetailsTransformed @ existing
                |> List.distinctBy key
                |> List.sortByDescending (_.dateTime)

            let addedCount = List.length merged - List.length existing

            tryWrite jsonPath merged
            |> Result.map (fun () -> addedCount)))

let updateNews codes startDate endDate : Result<int, string> =
    updateNewsCust "reports.json" codes startDate endDate

let loadPagedNewsWithCriteria jsonPath (criteria: NewsFilterCriteria) : Result<PagedResult<Details.NewsDetail>, string> =
    tryRead jsonPath
    |> Result.bind (fun details ->
        if details.IsEmpty then
            Error "No news details found in the JSON file."
        else
            Ok <| DetailsQuery.filterAll criteria details)