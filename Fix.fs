module Fix

open System.IO

let (+/) dir1 dir2 = Path.Combine(dir1, dir2)

let reclassify () =
    let pe = Predicitions.setPrediction

    let folder = "." +/ "report"
    Lib.filesFromDir folder
    |> Seq.iter (fun (sourcefile: string) ->
        let filename = Path.GetFileName sourcefile
        let newfolder = folder +/ Predicitions.predictFolder pe (filename)
        let destfile = newfolder +/ filename
        if sourcefile <> destfile && not (filename.EndsWith(".cfg")) then
            printfn "- Moving:\n\tfrom: %s\n\t  to: %s" sourcefile destfile
            Lib.createDir newfolder
            File.Move(sourcefile, destfile))

