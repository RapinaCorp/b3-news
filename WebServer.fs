module WebServer

open System
open System.IO
open System.Net
open System.Threading
open System.Net.NetworkInformation

let lastTcpPort = 65535

let findNextFreePort start ports : int option =
    let rec findNext n =
        if n > lastTcpPort then None
        else if Seq.contains n ports then findNext (n + 1)
        else Some n
    findNext start

// Return 'port' or, if it is already in use, return the next available port or None.
let findFreePort (port: int) : int option =
    if port < 1 || port > lastTcpPort then 
        None
    else
        let ipProperties = IPGlobalProperties.GetIPGlobalProperties()
        let listeners = ipProperties.GetActiveTcpListeners() |> Array.map _.Port |> Array.sort 
        findNextFreePort port listeners

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

        let filePath = request.Url.LocalPath.TrimStart('/')

        // Handle /pdf/<numeroProtocolo> by fetching the Enet document through Download
        if request.Url.AbsolutePath.StartsWith("/pdf/") then
            let numeroProtocolo = request.Url.AbsolutePath.Substring("/pdf/".Length)
            match System.Int32.TryParse numeroProtocolo with
            | false, _ ->
                response.StatusCode <- 400
                use sw = new StreamWriter(response.OutputStream)
                do! sw.WriteAsync("ID inválido para PDF.") |> Async.AwaitTask
            | true, id ->
                let! fetched = Download.fetchBytes (Download.Enet id)
                match fetched with
                | Ok (bytes, _) ->
                    response.ContentType <- "application/pdf"
                    response.ContentLength64 <- int64 bytes.Length
                    do! response.OutputStream.WriteAsync(bytes, 0, bytes.Length) |> Async.AwaitTask
                    do! response.OutputStream.FlushAsync() |> Async.AwaitTask
                | Error err ->
                    response.StatusCode <- Download.httpStatusForFetchErr err
                    use sw = new StreamWriter(response.OutputStream)
                    do! sw.WriteAsync(Download.describeFetchErr err) |> Async.AwaitTask
        else
            // Serve index.html if no specific file is requested
            let fileToServe =
                match filePath with
                | null
                | "" -> "index.html"
                | _ -> filePath
            try
                use fileStream = File.OpenRead fileToServe
                response.ContentLength64 <- fileStream.Length
                response.ContentType <- getMimeType fileToServe
                let buffer = Array.zeroCreate<byte> (32 * 1024)
                let mutable bytesRead = 0L
                while bytesRead < fileStream.Length do
                    let! read = fileStream.ReadAsync(buffer, 0, buffer.Length) |> Async.AwaitTask
                    do! response.OutputStream.WriteAsync(buffer, 0, read) |> Async.AwaitTask
                    bytesRead <- bytesRead + int64 read
                    System.Console.Write('.')
                do! response.OutputStream.FlushAsync() |> Async.AwaitTask
            with
            | :? FileNotFoundException ->
                response.StatusCode <- 404
                response.StatusDescription <- "File not found"
            | _ ->
                response.StatusCode <- 500
                response.StatusDescription <- "Internal server error"
        return ()
    }

let rec serveStaticFilesAsync (cts: CancellationTokenSource) (port: int) (usage: string) =
    async {
        use listener = new HttpListener()
        listener.Prefixes.Add("http://localhost:" + port.ToString() + "/")
        listener.Start()

        printfn "\nServer listening on http://localhost:%i/" port
        printfn "%s" usage

        let rec serverLoop () =
            Log.info "- Servidor iniciado."
            async {
                try
                    while not cts.Token.IsCancellationRequested do
                        let! context = listener.GetContextAsync().WaitAsync(cts.Token) |> Async.AwaitTask
                        do! handleRequestAsync context
                with
                | :? HttpListenerException when cts.Token.IsCancellationRequested -> ()
                | e ->
                    Log.alert ("- " + e.Message)
                    Log.alert "- Reiniciando servidor..."
                    do! Async.Sleep 1000
                    return! serverLoop ()
            }
        do! serverLoop ()
        Log.alert "- Servidor parado."
    }
