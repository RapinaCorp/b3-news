# Simplified B3News (`update-json` + `serve-api`) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a feature branch off `main` that strips out all commands and code not related to `update-json` and `serve-api`, leaving a lean, fully functioning service.

**Architecture:** We will create a new git branch `feature/simplified-update-json-serve-api`, delete dead F# modules (`Predictions.fs`, `WebServer.fs`, `Fix.fs`, `List.fs`, `HaskellInterop.fs`), remove the `ML/` project and its package/embedded resource references, trim `Download.fs` and `Program.fs`, remove dead static files (`index_template.html`, `reports_template.html`), update `README.md`, and verify all tests pass and both commands function.

**Tech Stack:** F# (.NET 10.0), ASP.NET Core, Thoth.Json.Net.

## Global Constraints
- Target Framework: `net10.0`
- Commands supported: `update-json` and `serve-api` only. All other commands and unsupported arguments show usage and exit 0.
- All existing tests in `B3News.Tests` must continue to compile and pass successfully.

---

## Tasks

### Task 1: Create Branch & Clean Up Dead Modules and Static Assets

**Files:**
- Delete: `Predictions.fs`
- Delete: `WebServer.fs`
- Delete: `Fix.fs`
- Delete: `List.fs`
- Delete: `HaskellInterop.fs`
- Delete: `index_template.html`
- Delete: `reports_template.html`
- Delete directory: `ML/`

**Interfaces:**
- Consumes: None
- Produces: Cleaned repository workspace

- [ ] **Step 1: Create and switch to the new feature branch**

```bash
git checkout -b feature/simplified-update-json-serve-api
```

- [ ] **Step 2: Delete unused F# module files and HTML templates**

```bash
git rm Predictions.fs WebServer.fs Fix.fs List.fs HaskellInterop.fs index_template.html reports_template.html
```

- [ ] **Step 3: Remove the ML directory and its contents**

```bash
rm -rf ML
git add -u
```

- [ ] **Step 4: Commit branch setup and deletions**

```bash
git commit -m "refactor: remove dead modules, templates, and ML project"
```

---

### Task 2: Trim `Download.fs` to Required Functions

**Files:**
- Modify: `Download.fs`

**Interfaces:**
- Consumes: None
- Produces: Trimmed `Download` module containing only `classify`, `describeFetchErr`, `httpStatusForFetchErr`, `fetchBytes`, `getRealUrl`, and `getHtmlLink`.

- [ ] **Step 1: Read current `Download.fs` to ensure exact matching**

Use `read` on `/home/adr/Work/github.com/RapinaCorp/b3-news/Download.fs` (already read previously, but check if needed).

- [ ] **Step 2: Edit `Download.fs` to remove `save`, `downloadAndSaveFileAsync`, `saveFileList`, `readFile`, `readFileList`, and `_outputDir`**

Replace the contents of `Download.fs` with only the retained types and functions (`classify`, `describeFetchErr`, `httpStatusForFetchErr`, `fetchBytes`, `getRealUrl`, `getHtmlLink`, and their private helpers).

- [ ] **Step 3: Verify build compiles `Download.fs`**

Run: `dotnet build B3News.fsproj`
Expected: Build errors due to references in other files (expected at this stage, or warnings).

- [ ] **Step 4: Commit trimmed `Download.fs`**

```bash
git add Download.fs
git commit -m "refactor(download): remove unused file storage and list persistence functions"
```

---

### Task 3: Update `Program.fs` to Expose Only `update-json` and `serve-api`

**Files:**
- Modify: `Program.fs`

**Interfaces:**
- Consumes: `Api.startApiServer`, `JsonStore.updateNewsCust`, `Config`, `Log`
- Produces: Entry point supporting only `update-json`, `serve-api`, and `help`/usage.

- [ ] **Step 1: Edit `Program.fs`**

Remove `download`, `run`, `waitForKeyPressAsync`, `listReports`, `serverUsage`, `createIndex`, `updateFiles`, `reportsFile`, `runServer`, `runDaemon`. Simplify `main` to handle only `update-json`, `serve-api`, and `help`/default usage.

- [ ] **Step 2: Verify build compiles `Program.fs`**

Run: `dotnet build B3News.fsproj`

- [ ] **Step 3: Commit `Program.fs` updates**

```bash
git add Program.fs
git commit -m "refactor(program): reduce CLI commands to update-json and serve-api"
```

---

### Task 4: Update `B3News.fsproj` Project File

**Files:**
- Modify: `B3News.fsproj`

**Interfaces:**
- Consumes: None
- Produces: Clean project file without deleted files, `Microsoft.ML`, or embedded resources.

- [ ] **Step 1: Edit `B3News.fsproj`**

Remove `<Compile Include="..." />` entries for `Predictions.fs`, `WebServer.fs`, `Fix.fs`, `List.fs`, `HaskellInterop.fs`. Remove `<EmbeddedResource Include="ML/data/B3NewsModel.zip" />` and `reports_template.html`. Remove `<PackageReference Include="Microsoft.ML" />`.

- [ ] **Step 2: Verify project build success**

Run: `dotnet build B3News.fsproj`
Expected: Build succeeds with 0 errors.

- [ ] **Step 3: Run test suite**

Run: `dotnet test B3News.Tests/B3News.Tests.fsproj`
Expected: All tests pass.

- [ ] **Step 4: Commit `B3News.fsproj` changes**

```bash
git add B3News.fsproj
git commit -m "chore(proj): remove deleted sources, ML package, and obsolete embedded resources"
```

---

### Task 5: Update Documentation (`README.md`)

**Files:**
- Modify: `README.md`

**Interfaces:**
- Consumes: None
- Produces: Updated README documenting only `update-json` and `serve-api`.

- [ ] **Step 1: Edit `README.md`**

Update "Como Usar" to document only `update-json` and `serve-api`. Remove ML generation prerequisite step. Remove other command descriptions.

- [ ] **Step 2: Commit documentation update**

```bash
git add README.md
git commit -m "docs: update README to reflect simplified two-command scope"
```

---

### Task 6: Final Verification & Smoke Tests

**Files:**
- None (execution smoke test)

**Interfaces:**
- Consumes: Built binary / dotnet run
- Produces: Verified working prototype

- [ ] **Step 1: Run build and tests**

Run: `dotnet build && dotnet test`
Expected: PASS

- [ ] **Step 2: Test CLI usage / no args**

Run: `dotnet run`
Expected: Prints usage and exits 0.

- [ ] **Step 3: Test update-json**

Run: `dotnet run -- update-json hoje`
Expected: Fetches today's news and updates `reports.json`.

- [ ] **Step 4: Final commit status check**

```bash
git status
```
