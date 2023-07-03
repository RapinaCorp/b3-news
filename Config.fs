module Config

open System.IO

let (+/) path1 path2 = Path.Combine(path1, path2)

let readLinesFromFile (fileName: string) : Option<string list> =
    if File.Exists(fileName) then
        Some(File.ReadAllLines(fileName) |> Array.toList)
    else
        None

let readTickersFromFile: string list =
    let currentDirectory = Directory.GetCurrentDirectory()
    let reportDirectory = currentDirectory +/ Download.outputDir
    let fileNames = [ "b3-news.cfg"; "b3news.cfg" ]

    let readLinesFromDirectory (directory: string) =
        fileNames
        |> List.map (fun fileName -> Path.Combine(directory, fileName))
        |> List.tryPick (fun file -> readLinesFromFile file)

    match readLinesFromDirectory currentDirectory with
    | Some config -> config
    | None -> readLinesFromDirectory reportDirectory |> Option.defaultValue []
