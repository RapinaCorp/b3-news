module WebServer

open System
open System.IO
open System.Net

let getMimeType (filePath: string) =
    match Path.GetExtension(filePath).ToLowerInvariant() with
    | ".html" -> "text/html"
    | ".txt" -> "text/plain"
    | ".pdf" -> "application/pdf"
    | _ -> "application/octet-stream" // Default MIME type for unknown extensions

let serveStaticFiles () =
    let listener = new HttpListener()
    listener.Prefixes.Add("http://localhost:8080/")
    listener.Start()

    printfn "Server listening on http://localhost:8080/"

    while true do
        let context = listener.GetContext()
        let request = context.Request
        let response = context.Response

        // Get the requested file path
        let filePath = request.Url.LocalPath.TrimStart('/')

        // Serve index.html if no specific file is requested
        let fileToServe =
            if String.IsNullOrEmpty(filePath) then
                "index.html"
            else
                filePath

        try
            let fileBytes = File.ReadAllBytes(fileToServe)
            response.ContentLength64 <- int64 fileBytes.Length
            response.ContentType <- getMimeType (fileToServe) // Set the MIME type
            response.OutputStream.Write(fileBytes, 0, fileBytes.Length)
        with
        | :? FileNotFoundException ->
            response.StatusCode <- 404
            response.StatusDescription <- "File not found"
            response.Close()
        | _ ->
            response.StatusCode <- 500
            response.StatusDescription <- "Internal server error"
            response.Close()
