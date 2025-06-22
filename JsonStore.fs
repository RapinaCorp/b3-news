module JsonStore

open System.Text.Json
open System.IO

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

let paginate (pageSize: int) (page: int) (items: 'a list) : 'a list * int =
    if pageSize <= 0 then
        ([], 0)
    else
        let pageIndex = max 0 (page - 1)
        let totalItems = List.length items
        let totalPages =
            if totalItems = 0 then 0
            else (totalItems + pageSize - 1) / pageSize

        let startIndex = pageIndex * pageSize

        if pageIndex >= totalPages && totalItems > 0 then
            ([], totalPages)
        elif totalItems = 0 then
            ([], 0)
        else
            let remainingItemsCount = totalItems - startIndex
            let itemsToTake = min pageSize remainingItemsCount
            let pagedItems =
                items
                |> List.skip startIndex
                |> List.take itemsToTake

            (pagedItems, totalPages)


let updateNews codes startDate endDate jsonPath : Result<int, string> =
    let key (x: Details.NewsDetail) = x.ticker, x.dateTime, x.filename, x.url

    Details.fetchNewsDetails codes startDate endDate
    |> Result.bind (fun newDetails ->
        tryRead jsonPath
        |> Result.bind (fun existing ->
            let merged =
                newDetails @ existing
                |> List.distinctBy key
                |> List.sortByDescending (_.dateTime)

            let addedCount = List.length merged - List.length existing

            tryWrite jsonPath merged
            |> Result.map (fun () -> addedCount)))

type SortOrder = Ascending | Descending

let loadPagedNewsCust codes page' pageSize' jsonPath sortByFn sortOrder : Result<Details.NewsDetail list * int, string> =
    tryRead jsonPath
    |> Result.bind (fun details ->
        if details.IsEmpty then
            Error $"No news details found in the JSON file."
        else
            let filteredDetails = 
                details |> List.filter (fun x -> List.contains x.ticker codes)
            let sorted =
                match sortOrder with
                | Ascending -> filteredDetails |> List.sortBy sortByFn
                | Descending -> filteredDetails |> List.sortByDescending sortByFn

            let paged, totalPages = paginate pageSize' page' sorted
            Ok (paged, totalPages))

let loadPagedNews codes page' pageSize' = 
    loadPagedNewsCust codes page' pageSize' "reports.json" (fun x -> x.dateTime) Descending