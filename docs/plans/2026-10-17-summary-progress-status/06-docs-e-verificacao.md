# Passo 6 — Documentação e verificação

## `AGENTS.md` — reescrever o gotcha 6

Substituir o texto atual do item 6 por (mantendo o formato de uma linha):

> 6. **Per-item summaries:** `NewsDetail.summary` (`""`/null = not summarized yet) is cached in `reports.json` itself — no TTL, published text never changes. `GET /api/news/{id}/summary` returns cached `{summary}` immediately; otherwise it starts an in-memory, one-per-id background job and returns `{status:"fetching"|"summarizing", message}`; both viewers poll until `{summary}` or an error. Steps come from `NewsSummary.summarizeDetail`'s `onStep` (extract → LLM). Job state (`Api.SummaryState`) is per-process, lost on restart; persist failure serves the summary once from memory (`Succeeded`) then removes the entry; a failed job replies the error once (`Failed of SummaryErr`, 502 generation / 500 store) and the next GET restarts generation (no retry query param). `/no_think` prefix gated by `DisableReasoning` (Nemotron-only, `NewsSummary.fs:8`); `SummarySettings` is just `Model|MaxChars|MaxRetries|DisableReasoning`. `updateSummary` serializes in-process writes with `writeLock`; `updateNewsCust` preserves cached summaries for re-fetched keys. Frontend: chevron + modal + poll loop in both viewers; `report_viewer_legacy.html` must stay iOS 9-safe (XHR, no `fetch`/`Promise`/`classList`/flex — see passo 4).

## `CONTEXT.md` — nova entrada no glossário

Adicionar após `SaveOutcome`:

```markdown
## SummaryJob
In-memory, per-comunicado summary generation state behind
`GET /api/news/{id}/summary` (not persisted; lost on process restart).
`SummaryState = Running of SummaryStep | Succeeded of string | Failed of SummaryErr`;
`SummaryStep = Fetching | Summarizing` (defined in `NewsSummary`).
One job per id: `ConcurrentDictionary.TryAdd` is the gate; `startSummaryJob`
returns false on a lost race. Only the owning pipeline writes transitions;
a successful persist removes the entry.
Terminal replies are idempotent: computed from the state read, then removed
best-effort — `Succeeded` (persist failed) serves the summary once, `Failed`
replies the error once so the next GET starts a fresh job.
`Fetching` = download/extract (html or pdf via `ContentExtractor`);
`Summarizing` = LLM call.
```

## Verificação

1. `dotnet build` — compilar tudo (os HTML são recursos embutidos: sem
   rebuild, o server continua servindo a versão antiga).
2. `dotnet test B3News.Tests --filter "FullyQualifiedName~ApiTests"` — suíte
   nova + testes mantidos.
3. `dotnet test` — suíte completa (a `IntegrationTests` é lenta/flaky e sobe
   servidor real na `:8082`; rodar mesmo assim e reportar).
4. Smoke manual (requer rede + `NVIDIA_API_KEY=nvapi-...`):
   - `dotnet run -- server`
   - `http://localhost:8082/` e `http://localhost:8082/legacy`:
     clicar ▾ em um comunicado sem resumo e observar
     "Gerando resumo…" → "Buscando o conteúdo do comunicado…" →
     "Gerando o resumo com IA…" → resumo em bullets;
     clicar de novo (mesma sessão) → instantâneo;
     recarregar a página e clicar de novo → instantâneo (cache no
     `reports.json`).
   - Fechar o modal no meio da geração e abrir outro id: nenhum texto do id
     antigo deve aparecer no novo popup.
   - Forçar erro (ex.: `NVIDIA_API_KEY` inválida em outro terminal ou item com
     URL quebrada): erro + "Tentar de novo" reinicia a geração.
5. Conferir `git diff` dos dois HTML para garantir que o legacy não ganhou
   sintaxe ES6+.

## Critério de aceite final

- Todos os itens do contrato do passo 0 observados no smoke manual.
- `dotnet test` verde (ou falha exclusivamente da `IntegrationTests`
  pré-existente lenta/flaky, reportada explicitamente).
- `AGENTS.md` e `CONTEXT.md` descrevem o comportamento novo.
