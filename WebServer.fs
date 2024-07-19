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

        // Get the requested file path
        let filePath = request.Url.LocalPath.TrimStart('/')

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
    }

let serveStaticFilesAsync (cts: CancellationTokenSource) (port: int) (usage: string) =
    async {
        use listener = new HttpListener()
        listener.Prefixes.Add("http://localhost:" + port.ToString() + "/")
        listener.Start()

        printfn "\nServer listening on http://localhost:%i/" port
        printfn "%s" usage

        try
            while not cts.Token.IsCancellationRequested do
                let! context = listener.GetContextAsync().WaitAsync(cts.Token) |> Async.AwaitTask
                do! handleRequestAsync context

        with
        | :? HttpListenerException when cts.Token.IsCancellationRequested -> ()
        | e -> Log.alert ("- " + e.Message)

        Log.alert "- Servidor parado."
    }
