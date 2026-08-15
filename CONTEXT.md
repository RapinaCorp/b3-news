# B3News — Domain Glossary

## Source
Discriminator for a B3 download-source URL, after classification.
Union: `Fnet of id:int | Enet of id:int | EnetNbc of nsd:string | Unknown of url:string`.
Replaces the implicit "every callee re-classifies a raw URL" pattern in `Download`.

## Kind
Content kind of bytes returned by B3 surfaces, after base64 decode.
`Pdf | Html`. Informational; callers decide policy per-site.

## FetchErr
Protocol-level failure modes of `Download.fetchBytes`:
`BadUrl of string | Network of exn | Upstream of int*string | Decode of string*string | Rejected of string | CaptchaRequired of string | Io of string`.
Upstream bodies are truncated to 256 chars at the `FetchErr` boundary so they
cannot leak full upstream payloads (base64 PDFs, JSON error bodies) into logs
or HTTP responses. `Decode` means content decoding failed (base64/json);
`Io` means a disk write failed in `Download.save`. `Download.describeFetchErr`
renders a short, log-safe message; exception messages are not truncated (they
do not carry upstream payloads). `Download.httpStatusForFetchErr` maps a
`FetchErr` to an HTTP status for client-facing responses: `Upstream` mirrors
the remote status, `Rejected` maps to 502, `CaptchaRequired` maps to 503,
network/decode/IO map to the nearest 4xx/5xx.

## SaveOutcome
Result of `Download.save`: `Written of path | Skipped of path`.
`Skipped` is a success — the file existed and was preserved.
Logged at `Log.notice` (not `Log.error`) in `Program.fs:19`.
`Skipped` does NOT contribute to the `files.lst` append — preserved from today's
behavior via `Program.download` returning `None` on `Skipped`.
