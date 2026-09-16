# Rename NvidiaClient.fs to OpenAIClient.fs and support multiple OpenAI-compatible providers

## Context

`NvidiaClient.fs` talks to NVIDIA's endpoint using the standard OpenAI chat
completions wire format (`choices[0].message.content`, `model`/`messages`/
`temperature`/`max_tokens`). Google's Gemini exposes an OpenAI-compatible
endpoint with the same request/response shape and the same
`Authorization: Bearer <key>` auth scheme, so this module can support both
providers (and any other OpenAI-compatible one) without changing its parsing
or retry logic — only the endpoint URL, the model name, and the API key
need to become configurable instead of hardcoded to NVIDIA.

Note: the exact current state of the codebase may have drifted since this
plan was written (other refactors may have landed). Treat file/line
references below as a starting point, and grep the project for the noted
symbol names to make sure every reference is caught before considering this
done.

## 1. Rename the file and module

- Rename `NvidiaClient.fs` to `OpenAIClient.fs`.
- Change `module NvidiaClient` to `module OpenAIClient` at the top of the file.
- In `B3News.fsproj`, change `<Compile Include="NvidiaClient.fs" />` to
  `<Compile Include="OpenAIClient.fs" />`, keeping the same position in the
  compile order (F# compiles files top-to-bottom in the order listed).
- Grep the whole project for `NvidiaClient.` (the module-qualified calls,
  e.g. `NvidiaClient.summarize`) and update every occurrence to
  `OpenAIClient.`. The project won't compile until all call sites are
  updated — let the compiler errors guide you to any you missed.

## 2. Stop hardcoding the NVIDIA endpoint

Replace the top-level:

```fsharp
let endpoint = "https://integrate.api.nvidia.com/v1/chat/completions"
```

with:

```fsharp
type Provider =
    | Nvidia
    | Gemini
    | Custom of endpoint: string

let endpointFor (provider: Provider) : string =
    match provider with
    | Nvidia -> "https://integrate.api.nvidia.com/v1/chat/completions"
    | Gemini -> "https://generativelanguage.googleapis.com/v1beta/chat/completions"
    | Custom url -> url
```

No changes are needed to `encodeRequest`, `parseChatContent`, or the retry
logic in `postWithRetry` — the wire format is identical across providers.
`reasoning_effort` (NVIDIA/Nemotron-specific) is silently ignored by
Gemini's endpoint when present, so it's safe to keep sending it
unconditionally based on the existing `disableReasoning` flag; no need to
make it provider-conditional.

## 3. Thread `Provider` through `postWithRetry` and `summarize`

- Add a `provider: Provider` parameter to both `postWithRetry` and
  `summarize`, in a position consistent with the existing parameter order
  (near `model`, since it plays a similar "which backend" role).
- Inside `postWithRetry`, replace the use of the old module-level `endpoint`
  value (in the `HttpRequestMessage` construction) with
  `endpointFor provider`.
- Update the doc comment above `summarize` to mention the new parameter.

## 4. Wire provider selection into config

- In `Config.fs`, add a `Provider: string` field to `SummarySettings`
  (e.g. `"nvidia"` or `"gemini"`), defaulting to `"nvidia"` in
  `defaultSummarySettings`, and parsed in `mergeSummary` the same way the
  other `Summary` fields are.
- Add a small parsing helper (in `OpenAIClient.fs` or `Config.fs`, whichever
  fits the project's existing conventions better):

  ```fsharp
  let providerFromString (s: string) : Provider =
      match s.ToLowerInvariant() with
      | "gemini" -> Gemini
      | "nvidia" -> Nvidia
      | other -> Custom other // unknown values are treated as a raw endpoint URL
  ```

- Add `"Provider": "nvidia"` to the `"Summary"` block in
  `b3news.settings.json` (optional, since it matches the default — but
  makes the option discoverable).
- **API key**: use a single generic environment variable, `OPENAI_API_KEY`,
  regardless of the selected provider — this matches the common convention
  of OpenAI-compatible tooling (the variable names the *protocol*, not the
  backend). Find the function that currently resolves the NVIDIA key
  (search for `nvidiaApiKey`, `nvidiaApiKeyEnv`, `NVIDIA_API_KEY` across the
  project), rename it to something provider-neutral (e.g. `openAiApiKey ()`),
  and point it at `OPENAI_API_KEY` instead. It no longer needs a `Provider`
  parameter — same variable is read no matter which provider is configured.
  Since there's only one key for potentially different backends, log the
  selected `Provider` (or its endpoint) alongside each request — the
  existing `label` already goes into the NVIDIA-call logs in
  `postWithRetry`; include the provider there too — so an auth failure
  after switching `Provider` without updating the key's value is easy to
  diagnose (a plain 401/403 alone won't hint that the key belongs to the
  other provider).

## 5. Update every call site

- Find wherever the per-document summary is generated (the code that calls
  `OpenAIClient.summarize`) and thread the configured `Provider` through,
  the same way `model` / `disableReasoning` / `maxRetries` are already
  passed from `Config.readSummarySettings()`.
- Do a final project-wide search for `NvidiaClient`, `nvidiaApiKey`,
  `NVIDIA_API_KEY`, and `nvidiaApiKeyEnv` to confirm nothing was missed.

## Acceptance criteria

- `dotnet build` compiles cleanly, with no leftover `NvidiaClient`
  references anywhere in the project.
- `"Provider": "nvidia"` (or the field omitted, using the default) generates
  a summary exactly as before — same endpoint, same `OPENAI_API_KEY` value
  as today.
- `"Provider": "gemini"`, with a valid Gemini API key set as
  `OPENAI_API_KEY` and a valid Gemini model name (e.g. `gemini-2.5-flash`),
  successfully generates a summary through the same code path — no
  special-casing needed outside of `OpenAIClient.fs` and the config.
- Logs make the active provider visible per request, so an auth failure
  caused by switching `Provider` without updating `OPENAI_API_KEY` is easy
  to tell apart from other failures.
- An invalid or unrecognized `Provider` value fails clearly and early
  (either at config-read time or at first use) rather than silently
  defaulting to the wrong endpoint.
