# Step 03 — `Download.fs`: `removeQuotes` off-by-one + `detectKind` memory

## Problem a — `removeQuotes` mangles `""` (`Download.fs:110-114`)

```fsharp
if body.StartsWith "\"" && body.EndsWith "\"" then
    body[1 .. (max 1 body.Length - 2)]
```

For the two-character body `""` (an empty JSON string), the slice is
`body[1..1]` → a lone `"`, which then fails `decodeBase64` with a confusing
`Decode("base64", ...)` error instead of a clean empty-body path.

## Fix a

```fsharp
let private removeQuotes (body: string) : string =
    if body.Length >= 2 && body.StartsWith "\"" && body.EndsWith "\"" then
        body.Substring(1, body.Length - 2)
    else
        body
```

Note: `body = "\"" (single quote char)` no longer matches (StartsWith and
EndsWith can't both hold for length 1) — correct.

## Problem b — `detectKind` allocates 2-3× document size (`Download.fs:148-152`)

```fsharp
let body = System.Text.Encoding.UTF8.GetString bytes
if body.ToUpper().Contains "%PDF" then ...
```

Full body → UTF-16 string → uppercase copy, just to sniff magic strings.
Multi-MB PDFs pay this twice (also from `ContentExtractor.fetchGeneric`).

## Fix b — bounded 64 KB sniff

`%PDF` magic is at offset 0 for real PDFs; `<html>` appears near the top
of any HTML wrapper page. Sniff the first 64 KB, case-insensitive, without
allocating strings larger than the window:

```fsharp
let private sniffWindow = 64 * 1024

let detectKind (bytes: byte[]) : Kind =
    let len = min bytes.Length sniffWindow
    let prefix = System.Text.Encoding.ASCII.GetString(bytes, 0, len).ToUpperInvariant()
    if prefix.Contains "%PDF" then Pdf
    elif prefix.Contains "<HTML>" then Html
    else Pdf
```

**Embedded decision:** 64 KB window instead of a full-body scan. If an
exotic document has `%PDF`/`<HTML>` only beyond 64 KB, it classifies as
`Pdf` (same default as today). Acceptable; `ContentExtractor` already
falls back to content-type heuristics for the generic path.

## Out of scope (owner verifying)

The Fnet branch of `fetchBytes` (base64-quoted JSON body assumption) is
left as-is pending a live check of `exibirDocumento`'s current response
format.

## Tests (`DownloadTests.fs`)

- `removeQuotes` is private — test through the public seam that exposes
  the behavior if one exists, otherwise make the test target `detectKind`
  only and cover the quote fix indirectly. Prefer: make `removeQuotes`
  internal-visible via `InternalsVisibleTo`? **No** — keep the module
  surface minimal. The Fnet path already has tests (they pin the
  base64-quoted behavior); the `""` case flows through `fetchBytes` which
  needs network. Practical call: add `detectKind` tests (PDF bytes, HTML
  bytes, 64KB-boundary case: `<HTML>` at offset just inside/outside the
  window) and rely on code review for `removeQuotes` (3-line function).

## Verification

- `dotnet build`
- `dotnet test B3News.Tests --filter "FullyQualifiedName~DownloadTests"`
