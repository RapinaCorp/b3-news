# Simplified B3News: `update-json` + `serve-api` Only

## 1. Goal & Scope

Create a new feature branch off `main` that produces a smaller, focused program exposing only the two commands `update-json` and `serve-api`. Everything related to file downloading, indexing, report generation, daemon/serve, reclassification, ML, and ad-hoc date-based downloads is removed. The remaining two commands must compile and run identically to today.

## 2. Branch Strategy

- `git checkout -b feature/simplified-update-json-serve-api` from `main`.
- All changes are deletions/edits on this branch; no rewrite of working code beyond trimming.

## 3. Module Graph After the Cut

**Kept (topological order):**
```
Log → Lib → News → Details → DetailsQuery → Download → Config → JsonStore → Api → Program
```

**Removed modules entirely:**
- `Predictions.fs`
- `WebServer.fs`
- `Fix.fs`
- `List.fs`
- `HaskellInterop.fs`
- The entire `ML/` project (`ML/SingleLabel.fs`, `ML/MultiLabel.fs`, `ML/Lib.fs`)

## 4. `Download.fs` — Hard Trim

**Retained (only what `serve-api` / `JsonStore` / tests need):**

- **Types/values:** `Source`, `FetchErr`, `Kind`, `SaveOutcome` (type retained; its `Written`/`Skipped` cases are no longer constructed, but keeping the type avoids churn)
- **Functions:**
  - `classify` (+ `patternFnet`, `patternEnet`, `patternEnetNbc`, `enetApiUrl`)
  - `describeFetchErr`
  - `httpStatusForFetchErr`
  - `fetchBytes` (+ all its private dependencies: `httpClient`, `truncate`, `removeQuotes`, `withRetry`, `getWithRetry`, `postWithRetry`, `detectKind`, `bindAsync`, `readBody`, `decodeBase64`, `parseIpeResponse`)
  - `getRealUrl`
  - `getHtmlLink` (kept so `DownloadTests.fs` unit tests pass)

**Removed from `Download.fs`:**
- `_outputDir`
- `save`
- `downloadAndSaveFileAsync`
- `filesListFilename`
- `saveFileList`
- `readFile`
- `readFileList`

## 5. `Program.fs` — Reduce to Two Commands

**Retained functions:**
- `isDateFormat`
- `usage` (rewritten to list only `update-json` and `serve-api`)
- `updateJson`
- `main` (no `rec` / `and` chain):

```fsharp
| "serve-api" -> Api.startApiServer args[1..]
| "update-json" -> updateJson args[1..]
| _ -> usage ()   (* unknown command or no-args: show usage, exit 0 *)
```

**Removed from `Program.fs`:**
- `download`
- `run`
- `waitForKeyPressAsync`
- `listReports`
- `serverUsage`
- `createIndex`
- `updateFiles`
- `reportsFile`
- `runServer`
- `runDaemon`

## 6. `B3News.fsproj` Changes

- **Remove `<Compile Include="...">` entries for:** `Predictions.fs`, `WebServer.fs`, `Fix.fs`, `List.fs`, `HaskellInterop.fs`
- **Remove** `<EmbeddedResource Include="ML/data/B3NewsModel.zip" />`
- **Remove** `<EmbeddedResource Include="reports_template.html" />` (no longer used)
- **Keep** `<EmbeddedResource Include="report_viewer.html" />`? No — these are read from the filesystem by `Api.handleHtmlFile`, so they remain as loose files at repo root.
- **Remove** `<PackageReference Include="Microsoft.ML" Version="3.0.0-preview.22621.2" />`
- **Keep** `Thoth.Json.Net`, `Microsoft.Extensions.Configuration.Json`, `Microsoft.AspNetCore.App` FrameworkReference
- Publish/AOT settings unchanged

## 7. `B3News.Tests` Project

**All four test files remain and compile against the trimmed codebase:**

- `DetailsQueryTests.fs` → uses only `DetailsQuery`/`Details` (kept)
- `ApiTests.fs` → uses only `Api`/`JsonStore`/`DetailsQuery`/`Details` (kept)
- `DownloadTests.fs` → uses `Download.getHtmlLink` (kept)
- `IntegrationTests.fs` → uses `Download`/`Details`/`Api` paths (kept)

The `ML/` project is removed entirely; no solution file exists, so no project-reference cleanup needed.

## 8. Static Assets & Runtime Files Kept

- `report_viewer.html`
- `report_viewer_legacy.html`
- `reports.json` (example data)
- `b3news.settings.json`
- `b3news.service`
- `news_cache/`
- `.gitignore`
- `.editorconfig`
- `README.md`
- `CONTEXT.md`
- `LICENSE`

**Dropped:**
- `index_template.html` (was only used by `List.fs`)

## 9. Documentation Updates

**README.md:**
- Rewrite "Como Usar" section to document only:
  - `dotnet run update-json <data1> [<data2>]`
  - `dotnet run serve-api`
- Remove the ML model generation prerequisite (step 2 in current README)
- Remove all other command examples (`serve`, `daemon`, `build-index`, `generate-report`, `reclassify`, `list-resources`)

**CONTEXT.md:**
- Minor staleness acceptable: the doc references `Program.fs:19` (Log.notice for Skipped) and `Download.save` — both removed. Can be updated later if desired, but not required for this scope.

## 10. Verification Criteria

Build and test pass:
```bash
dotnet build B3News.fsproj
dotnet test  B3News.Tests/B3News.Tests.fsproj
dotnet run --project . --                      # prints usage, exits 0
dotnet run --project . -- serve-api            # boots at http://localhost:8082
dotnet run --project . -- update-json hoje      # fetches today, merges into reports.json
```

## 11. Out of Scope

- No new features
- No refactor of surviving code beyond deletions
- No API changes
- No behavior change beyond removal