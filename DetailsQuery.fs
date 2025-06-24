module DetailsQuery

open System
open System.Globalization
open Details // for NewsDetail

// Sort field options
type SortField =
    | ByDate
    | ByTicker
    | ByFilename
    | ByReportType
    | ByUrl

type SortDirection =
    | Asc
    | Desc

type NewsFilterCriteria =
    { Tickers: string list
      StartDate: string
      EndDate: string
      ReportType: string
      SortField: SortField option
      SortDirection: SortDirection
      Page: int
      PageSize: int }

type PagedResult<'a> = { Items: 'a list; TotalPages: int }

// Factory
let newCriteria () =
    { Tickers = []
      StartDate = ""
      EndDate = ""
      ReportType = ""
      SortField = None
      SortDirection = Desc
      Page = 1
      PageSize = 50 }

// Builder functions
let withTickers tickers criteria = { criteria with Tickers = tickers }

let withDateRange startDate endDate criteria =
    { criteria with
        StartDate = startDate
        EndDate = endDate }

let withReportType reportType criteria =
    { criteria with
        ReportType = reportType }

let withSortBy field direction criteria =
    { criteria with
        SortField = Some field
        SortDirection = direction }

let withPagination page pageSize criteria =
    { criteria with
        Page = page
        PageSize = pageSize }

// Filtering functions
let filterByCodes codes =
    let codeSet = Set codes
    fun (x: NewsDetail) -> not (String.IsNullOrEmpty x.ticker) && codeSet.Contains x.ticker

let filterByDate d1 d2 =
    let d1Date = DateTime.ParseExact(d1, "yyyy-MM-dd", CultureInfo.InvariantCulture)
    let d2Date = DateTime.ParseExact(d2, "yyyy-MM-dd", CultureInfo.InvariantCulture)
    let tryParseDateTime (s: string) =
        match DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None) with
        | true, dt -> Some dt
        | _ -> None
    fun (x: NewsDetail) ->
        match tryParseDateTime x.dateTime with
        | Some xDate -> xDate >= d1Date && xDate <= d2Date
        | None -> false

let filterByReportType (reportType: string) =
    let target = reportType.ToLowerInvariant()
    fun (x: NewsDetail) ->
        not (String.IsNullOrEmpty x.reportType)
        && x.reportType.ToLowerInvariant().Contains(target)

// Safe pagination
let paginate (pageSize: int) (page: int) (items: 'a list) : PagedResult<'a> =
    if pageSize <= 0 then
        { Items = []; TotalPages = 0 }
    else
        let pageIndex = max 0 (page - 1)
        let totalItems = List.length items
        let totalPages =
            if totalItems = 0 then
                0
            else
                (totalItems + pageSize - 1) / pageSize

        let startIndex = pageIndex * pageSize

        if pageIndex >= totalPages && totalItems > 0 then
            { Items = []; TotalPages = totalPages }
        elif totalItems = 0 then
            { Items = []; TotalPages = 0 }
        else
            let remainingItemsCount = totalItems - startIndex
            let itemsToTake = min pageSize remainingItemsCount
            let pagedItems = items |> List.skip startIndex |> List.take itemsToTake

            { Items = pagedItems
              TotalPages = totalPages }

// Sorting
let sortNews (field: SortField) (direction: SortDirection) (items: NewsDetail list) =
    let sortFn =
        match field with
        | ByDate ->
            items
            |> List.choose (fun x ->
                match
                    DateTime.TryParseExact(
                        x.dateTime,
                        "yyyy-MM-dd HH:mm:ss",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None
                    )
                with
                | true, dt -> Some(dt, x)
                | _ -> None)
            |> List.sortBy fst
            |> List.map snd

        | ByTicker -> items |> List.sortBy (fun x -> x.ticker)
        | ByFilename -> items |> List.sortBy (fun x -> x.filename)
        | ByReportType -> items |> List.sortBy (fun x -> x.reportType)
        | ByUrl -> items |> List.sortBy (fun x -> x.url)

    match direction with
    | Asc -> sortFn
    | Desc -> List.rev sortFn

// Main function
let filterAll (criteria: NewsFilterCriteria) (details: NewsDetail list) : PagedResult<NewsDetail> =
    let filters =
        [ if criteria.Tickers <> [] then
              yield filterByCodes criteria.Tickers
          if
              not (String.IsNullOrWhiteSpace criteria.StartDate)
              && not (String.IsNullOrWhiteSpace criteria.EndDate)
          then
              yield filterByDate criteria.StartDate criteria.EndDate
          if not (String.IsNullOrWhiteSpace criteria.ReportType) then
              yield filterByReportType criteria.ReportType ]

    let filtered =
        details
        |> Array.ofList
        |> Array.Parallel.choose (fun item ->
            if filters |> List.forall (fun f -> f item) then
                Some item
            else
                None)
        |> Array.toList

    let sorted =
        match criteria.SortField with
        | Some field -> sortNews field criteria.SortDirection filtered
        | None -> filtered

    paginate criteria.PageSize criteria.Page sorted
