module List

open System.IO
open System.Text
open System.Text.RegularExpressions

let (+/) path1 path2 = Path.Combine(path1, path2)

let stripLastUnderscore (str: string) =
    if str.Length > 1 && str.[str.Length - 1] = '_' then
        str.[0 .. str.Length - 2]
    else
        str

let listFilesWithRelativePaths (directoryPath: string) : string list =
    let files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories)
    [ for file in files -> file.Replace(directoryPath, "") ]

type Report =
    { date: string
      company: string
      name: string
      url: string }

let tryParseReportFromFilename (url: string) : Report option =
    let pattern =
        @"([\d_]{10})[\d_]{10}(FII_)*([A-Z0-9]+[A-Z0-9_]+)_((?:AGO_E_)?[A-Z0-9\.][a-z0-9\.][A-Za-z0-9_\.]+)"
    let options = RegexOptions.None
    let matches = Regex.Matches(url, pattern, options)
    if matches.Count > 0 && matches.[0].Groups.Count > 3 then
        let m = matches.[0]
        Some
            { date = (m.Groups.[1].ToString() |> stripLastUnderscore)
              company = (m.Groups.[2].ToString() + m.Groups.[3].ToString() |> stripLastUnderscore)
              name = (m.Groups.[4].ToString() |> stripLastUnderscore)
              url = url }
    else
        None

let listReports (directoryPath: string) =
    listFilesWithRelativePaths directoryPath
    |> List.choose tryParseReportFromFilename

let createHtmlTable (company: string) (companyReports: Report list) =
    let tableHeader =
        sprintf "<h2>%s</h2><table><tr><th>Date</th><th>Report</th></tr>\n" company
    let tableRows =
        companyReports
        |> List.map (fun report ->
            sprintf "<tr><td>%s</td><td><a href='%s'>%s</a></td></tr>\n" report.date report.url report.name)
        |> String.concat ""
    let tableFooter = "</table><br />"
    tableHeader + tableRows + tableFooter

let head =
    "<head>\
    <meta charset=\"UTF-8\">\
    <style>\
        body{max-width:max-content;margin:auto;font-family:system-ui,-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Oxygen,Ubuntu,Cantarell,'Open Sans','Helvetica Neue',sans-serif}\
        h2,table{margin-left:auto;margin-right:auto}\
        table{border-collapse:collapse;margin:25px 0;font-size:.9em;font-family:sans-serif;min-width:600px;box-shadow:0 0 20px rgba(0,0,0,.15)}\
        thead tr{background-color:#009879;color:#fff;text-align:left}\
        td,th{padding:12px 15px}\
        tbody tr{border-bottom:1px solid #ddd}\
        tbody tr:nth-of-type(2n){background-color:#f3f3f3}\
        tbody tr:last-of-type{border-bottom:2px solid #009879}\
        tbody tr.active-row{font-weight:700;color:#009879}\
    </style>\
    </head>"

let printHtml (directoryPath: string) =
    // let ppp = System.Text.CodePagesEncodingProvider.Instance
    // Encoding.RegisterProvider(ppp)
    // let windows1252 = Encoding.GetEncoding("CP850")
    // let utf8 = Encoding.UTF8

    System.Console.OutputEncoding <- Encoding.UTF8

    listReports directoryPath
    |> List.sortBy (fun r -> r.company, r.date, r.name)
    |> List.groupBy (fun r -> r.company)
    |> List.map (fun (company, companyReports) -> createHtmlTable company companyReports)
    |> String.concat ""
    |> printfn "<!DOCTYPE html><html lang=\"pt\">%s<body>%s</body></html>" head

// |> List.iter (fun r -> printfn "%-20s\t%-60s\t%s" r.company r.name r.date)

let loadTemplate (filename: string) : Option<string> =
    // Check if files exists and, if so, read it
    let currentDirectory = Directory.GetCurrentDirectory()
    let filePath = currentDirectory +/ filename
    if File.Exists(filePath) then
        Some(File.ReadAllText filePath)
    else
        None

let createIndexHtml (files: (string * string) list) =
    match loadTemplate "index_template.html" with
    | Some template ->
        use outfile = System.IO.File.CreateText("index.html")
        files
        |> List.sortByDescending (fun (_, filename) -> Path.GetFileName filename)
        |> List.sortBy fst
        |> List.map (fun (ticker, filename) ->
            sprintf "  {name: '%s', url: '%s'}," ticker (filename.Replace("\\", "/")))
        |> fun lines -> fprintf outfile "%s" (template.Replace("{content}", String.concat "\n" lines))
    | None -> Log.error "Template not found"
