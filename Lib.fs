module Lib

open System
open System.Text.RegularExpressions

let safe (ch : char) : char =
    if Regex.IsMatch(string(ch), "[^0-9a-zA-Z._]") then '_'
    else ch

let removeExtra_ (txt : string) : string =
    let go (x : char) (acc : string) : string =
        string x + (if x = '_' then acc.TrimStart '_' else acc)

    if String.IsNullOrEmpty txt then ""
    else Seq.foldBack go txt "" 

let removeLast_ (txt : string) : string =
    txt.TrimEnd '_'

let safeFilename (filename : string) : string option =
    match filename with
    | "" -> None
    | _ ->
        let fn = filename |> String.map safe |> removeExtra_ |> removeLast_
        if not (String.IsNullOrEmpty fn) then Some(fn) else None

// Function to list all files recursively from a given path
let rec filesFromDir (path: string) : seq<string> =
    seq {
        // Get all the files in the current directory
        for file in System.IO.Directory.GetFiles(path) do
            yield file

        // Recursively get files from all subdirectories
        for subdir in System.IO.Directory.GetDirectories(path) do
            yield! filesFromDir subdir
    }

let createDir (directoryPath: string) =
    if not (System.IO.Directory.Exists(directoryPath)) then
        System.IO.Directory.CreateDirectory(directoryPath) |> ignore