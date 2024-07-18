module WebServer

open System
open System.IO
open System.Net
open System.Threading

let getMimeType (filePath: string) =
    match Path.GetExtension(filePath).ToLowerInvariant() with
    | ".html" -> "text/html; charset=utf-8"
    | ".txt" -> "text/plain; charset=utf-8"
    | ".pdf" -> "application/pdf"
    | _ -> "application/octet-stream" // Default MIME type for unknown extensions

let handleRequestAsync (context: HttpListenerContext) =
    async {
        let request = context.Request
        use response = context.Response

        // Get the requested file path
        let filePath = request.Url.LocalPath.TrimStart('/')

        // Serve index.html if no specific file is requested
        let fileToServe =
            if String.IsNullOrEmpty(filePath) then
                "index.html"
            else
                filePath

        try
            let! fileBytes = File.ReadAllBytesAsync fileToServe |> Async.AwaitTask
            response.ContentLength64 <- int64 fileBytes.Length
            response.ContentType <- getMimeType (fileToServe) // Set the MIME type
            do!
                response.OutputStream.WriteAsync(fileBytes, 0, fileBytes.Length)
                |> Async.AwaitTask
            do! response.OutputStream.FlushAsync() |> Async.AwaitTask
        with
        | :? FileNotFoundException ->
            response.StatusCode <- 404
            response.StatusDescription <- "File not found"
        | _ ->
            response.StatusCode <- 500
            response.StatusDescription <- "Internal server error"
    }

let serveStaticFilesAsync (port: int) (cts: CancellationTokenSource) =
    async {
        use listener = new HttpListener()
        listener.Prefixes.Add("http://localhost:" + port.ToString() + "/")
        listener.Start()

        printfn "\nServer listening on http://localhost:%i/" port

        try
            while not cts.Token.IsCancellationRequested do
                let! context = listener.GetContextAsync().WaitAsync(cts.Token) |> Async.AwaitTask
                do! handleRequestAsync context

        with
        | :? HttpListenerException when cts.Token.IsCancellationRequested -> ()
        | e -> Log.alert e.Message

        Log.info "Listener stopped."
    }
