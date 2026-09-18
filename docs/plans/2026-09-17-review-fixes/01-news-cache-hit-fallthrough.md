# Step 01 — `News.fs`: cache-hit fallthrough bug + shared HttpClient

## Problem

`News.fetchAllNews` (`News.fs:54-56`):

```fsharp
match Decode.Auto.fromString<NwsMsgWrapper list> cached with
| Ok data -> return Ok data |> ignore
| Error _ -> File.Delete(cacheFile) |> ignore
```

`return Ok data |> ignore` returns `unit`, not `Ok data` (the `|> ignore`
binds to the expression *before* `return`). `AsyncBuilder.Combine` then
sequences the rest of the body, so a **valid cache hit still fetches from
the network** and overwrites the cache. Worse: if the network fetch then
fails, the caller gets `Error` even though a perfectly good cached result
was in hand. The `|> ignore` reads like the author wanted early return but
the code does the opposite.

## Fix

1. Extract the network fetch into a local async (`fetchFresh`) so the
   cache path can `return!`/`return` without falling through:

```fsharp
let private httpClient = new HttpClient()   // module level, see below

let fetchAllNews (d1: string) (d2: string) =
    async {
        let startDate = parseDate d1
        let endDate = parseDate d2
        if (endDate - startDate).TotalDays > 93.0 then
            return Error "O intervalo máximo permitido é de 3 meses."
        else
            Directory.CreateDirectory(cacheDir) |> ignore
            let cacheFile = getCacheFileName d1 d2

            let fetchFresh () =
                async {
                    let query = System.Web.HttpUtility.ParseQueryString("")
                    query.Add("agencia", "18")
                    query.Add("dataInicial", d1)
                    query.Add("dataFinal", d2)
                    let uriBuilder =
                        UriBuilder("https://sistemasweb.b3.com.br/PlantaoNoticias/Noticias/ListarTitulosNoticias")
                    uriBuilder.Query <- query.ToString()
                    try
                        let! response = httpClient.GetAsync(uriBuilder.Uri) |> Async.AwaitTask
                        response.EnsureSuccessStatusCode() |> ignore
                        let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                        match Decode.Auto.fromString<NwsMsgWrapper list> content with
                        | Ok data ->
                            do! File.WriteAllTextAsync(cacheFile, content) |> Async.AwaitTask
                            return Ok data
                        | Error err -> return Error <| sprintf "Erro ao listar notícias: %s" err
                    with ex ->
                        return Error(sprintf "HTTP error: %s" ex.Message)
                }

            if File.Exists(cacheFile) then
                let! cached = File.ReadAllTextAsync(cacheFile) |> Async.AwaitTask
                match Decode.Auto.fromString<NwsMsgWrapper list> cached with
                | Ok data -> return Ok data
                | Error _ ->
                    // corrupted cache: delete and fall through to a fresh fetch
                    File.Delete(cacheFile) |> ignore
                    return! fetchFresh ()
            else
                return! fetchFresh ()
    }
```

2. Replace the per-call `use client = new HttpClient()` with a module-level
   `let private httpClient = new HttpClient()` — `Details.fs`, `Download.fs`,
   `OpenAIClient.fs` and `ContentExtractor.fs` already use module-level
   clients; per-call instances cause socket churn. A static module-level
   `HttpClient` in F# is fine (the module init is a singleton).

## Tests

Add `NewsTests.fs` to `B3News.Tests` (add to `.fsproj` compile list in
dependency position — after `News.fs`-dependent modules, near the other
`*Tests.fs` files):

- Factor a testable seam: a pure function that decides cache handling,
  e.g. `News.tryParseCache : string -> Result<NwsMsgWrapper list, string>`
  (just `Decode.Auto.fromString`), unit-tested with valid/invalid JSON.
- Cache-file flow test: `getCacheFileName` is CWD-relative; tests already
  use temp dirs elsewhere. Write a valid cache JSON to a temp path, call
  `fetchAllNews` with a stubbed... — since the fetch client is not
  injectable, cover the decision logic via `tryParseCache` and rely on the
  restructured flow being trivially readable. Do **not** introduce DI
  machinery for this.

## Verification

- `dotnet build`
- `dotnet test B3News.Tests --filter "FullyQualifiedName~NewsTests"`
