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
      Filename: string
      ReportType: string
      SortField: SortField option
      SortDirection: SortDirection
      Page: int
      PageSize: int
      ClientRecordCount: int option }

type PagedResult<'a> =
    { Items: 'a list
      TotalPages: int
      TotalRecords: int }

// Factory
let newCriteria () =
    { Tickers = []
      StartDate = ""
      EndDate = ""
      Filename = ""
      ReportType = ""
      SortField = None
      SortDirection = Desc
      Page = 1
      PageSize = 50
      ClientRecordCount = None }

// Builder functions
let withTickers tickers criteria = { criteria with Tickers = tickers }

let withDateRange startDate endDate criteria =
    { criteria with
        StartDate = startDate
        EndDate = endDate }

let withFilename filename criteria = { criteria with Filename = filename }

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

let withClientRecordCount countOpt criteria =
    match countOpt with
    | Some count when count > 0 ->
        { criteria with
            ClientRecordCount = Some count }
    | _ ->
        { criteria with
            ClientRecordCount = None }

// Filtering functions
let filterByCodes codes =
    let codeSet = Set codes
    fun (x: NewsDetail) -> not (String.IsNullOrEmpty x.ticker) && codeSet.Contains x.ticker

let filterByDate (d1: string) (d2: string) =
    let dateFormats = [| "yyyy-MM-dd"; "dd'/'MM'/'yyyy"; "d'/'M'/'yyyy" |]

    let tryParseToIso (s: string) : string option =
        if String.IsNullOrWhiteSpace s then
            None
        else
            match DateTime.TryParseExact(s, dateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None) with
            | true, dt -> Some(dt.ToString("yyyy-MM-dd"))
            | false, _ -> None

    let d1Opt = tryParseToIso d1
    let d2Opt = tryParseToIso d2

    let (startDate, endDate) =
        match d1Opt, d2Opt with
        | Some d1', Some d2' when d1' <> d2' -> (min d1' d2', max d1' d2')
        | Some d, _
        | _, Some d -> (d, d)
        | _ -> ("0000-01-01", "9999-12-31") // wildcard

    fun (x: NewsDetail) ->
        let date = x.dateTime.Substring(0, 10)
        not (String.IsNullOrWhiteSpace x.dateTime)
        && date >= startDate
        && date <= endDate

let filterByFilename (filename: string) =
    let target = filename.ToLowerInvariant()
    fun (x: NewsDetail) ->
        not (String.IsNullOrEmpty x.filename)
        && x.filename.ToLowerInvariant().Contains(target)

let filterByReportType (reportType: string) =
    let target = reportType.ToLowerInvariant()
    fun (x: NewsDetail) ->
        not (String.IsNullOrEmpty x.reportType)
        && x.reportType.ToLowerInvariant().Contains(target)

// Safe pagination
let paginate (pageSize: int) (page: int) (items: 'a list) : PagedResult<'a> =
    let totalItems = List.length items
    if pageSize <= 0 then
        { Items = []
          TotalPages = 0
          TotalRecords = totalItems }
    else
        let pageIndex = max 0 (page - 1)
        let totalPages =
            if totalItems = 0 then
                0
            else
                (totalItems + pageSize - 1) / pageSize

        let startIndex = pageIndex * pageSize

        if pageIndex >= totalPages && totalItems > 0 then
            { Items = []
              TotalPages = totalPages
              TotalRecords = totalItems }
        elif totalItems = 0 then
            { Items = []
              TotalPages = 0
              TotalRecords = 0 }
        else
            let remainingItemsCount = totalItems - startIndex
            let itemsToTake = min pageSize remainingItemsCount
            let pagedItems = items |> List.skip startIndex |> List.take itemsToTake

            { Items = pagedItems
              TotalPages = totalPages
              TotalRecords = totalItems }

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

          if not (String.IsNullOrWhiteSpace criteria.Filename) then
              yield filterByFilename criteria.Filename

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

    // Limit dataset length if clientRecordCount is provided (apply after filtering, before sorting)
    let limited =
        match criteria.ClientRecordCount with
        | Some n when n > 0 -> filtered |> List.truncate n
        | _ -> filtered

    let sorted =
        match criteria.SortField with
        | Some field -> sortNews field criteria.SortDirection limited
        | None -> limited

    paginate criteria.PageSize criteria.Page sorted
