module Config

open System.IO
open System.Text.Json

let configFileName = "b3news.settings.json"

let (+/) path1 path2 = Path.Combine(path1, path2)

let readTickersFromFile () : string list =
    let currentDirectory = Directory.GetCurrentDirectory()
    let appSettingsPath = Path.Combine(currentDirectory, configFileName)
    if File.Exists(appSettingsPath) then
        let json = File.ReadAllText(appSettingsPath)
        try
            let doc = JsonDocument.Parse(json)
            match doc.RootElement.TryGetProperty("Tickers") with
            | true, tickersElem when tickersElem.ValueKind = JsonValueKind.Array ->
                tickersElem.EnumerateArray()
                |> Seq.filter (fun v -> v.ValueKind = JsonValueKind.String)
                |> Seq.map (fun v -> v.GetString())
                |> Seq.filter (fun s -> not (isNull s))
                |> Seq.toList
            | _ -> []
        with _ ->
            []
    else
        []
