module SummaryPage

open System
open System.IO
open System.Net

open SummaryPipeline

let private esc (s: string) =
    WebUtility.HtmlEncode(if isNull s then "" else s)

let private formatDate (dt: DateTime) =
    if dt = DateTime.MinValue then
        ""
    else
        dt.ToString("yyyy-MM-dd HH:mm")

let private comunicadoHtml (c: ComunicadoResumo) =
    $"""
        <li class="border-b border-gray-200 dark:border-gray-700 py-4 last:border-0">
          <div class="flex flex-wrap items-baseline gap-x-3 gap-y-1 text-sm text-gray-500 dark:text-gray-400">
            <time>{esc (formatDate c.Data)}</time>
            <a href="{esc c.Url}" target="_blank" rel="noopener noreferrer"
               class="text-blue-600 dark:text-blue-400 hover:underline font-medium">{esc c.Titulo}</a>
          </div>
          <p class="mt-2 whitespace-pre-line text-gray-800 dark:text-gray-200">{esc c.Resumo}</p>
        </li>"""

let private ativoHtml (a: AtivoResumo) =
    let falhas =
        if a.Falhas <= 0 then
            ""
        else
            $"""<p class="mt-3 text-sm text-amber-700 dark:text-amber-400">{a.Falhas} comunicado(s) não puderam ser processados.</p>"""
    let lista =
        if List.isEmpty a.Comunicados then
            """<p class="text-sm text-gray-500 dark:text-gray-400">Nenhum comunicado resumido.</p>"""
        else
            $"""<ul class="mt-2">{a.Comunicados |> List.map comunicadoHtml |> String.concat ""}</ul>"""
    $"""
    <article class="bg-white dark:bg-gray-800 rounded-xl shadow-lg p-6 space-y-4">
      <h2 class="text-2xl font-semibold text-gray-800 dark:text-gray-100">{esc a.Ativo}</h2>
      <div class="rounded-lg bg-blue-50 dark:bg-blue-950/40 border border-blue-100 dark:border-blue-900 p-4">
        <h3 class="text-sm font-semibold uppercase tracking-wide text-blue-700 dark:text-blue-300 mb-2">Resumo consolidado</h3>
        <p class="whitespace-pre-line text-gray-800 dark:text-gray-200">{esc a.ResumoConsolidado}</p>
      </div>
      <div>
        <h3 class="text-sm font-semibold uppercase tracking-wide text-gray-500 dark:text-gray-400">Comunicados</h3>
        {lista}
        {falhas}
      </div>
    </article>"""

let render (generatedAt: DateTime) (ativos: AtivoResumo list) : string =
    let stamp = generatedAt.ToString("yyyy-MM-dd HH:mm")
    let body =
        if List.isEmpty ativos then
            """<p class="text-gray-600 dark:text-gray-300">Nenhum ativo selecionado.</p>"""
        else
            ativos |> List.map ativoHtml |> String.concat "\n"
    $$$"""
<!DOCTYPE html>
<html lang="pt-BR" class="light">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Resumo de comunicados</title>
  <link rel="icon" href="data:image/svg+xml,%3Csvg xmlns='http://w3.org' viewBox='0 0 64 64'%3E%3Ctext y='52' font-size='52'%3E%F0%9F%A6%85%3C/text%3E%3C/svg%3E">
  <script src="https://tailwindcss.com"></script>
  <script>tailwind.config = {{ darkMode: 'class' }};</script>
  <link rel="preconnect" href="https://googleapis.com">
  <link href="https://googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap" rel="stylesheet">
  <style>body { font-family: 'Inter', sans-serif; }</style>
</head>
<body class="bg-gray-100 text-gray-900 dark:bg-gray-900 dark:text-gray-100 min-h-screen p-4">
  <div class="max-w-3xl mx-auto space-y-6">
    <header class="flex flex-col sm:flex-row sm:items-baseline sm:justify-between gap-2 pb-4 border-b border-gray-200 dark:border-gray-700">
      <div>
        <h1 class="text-2xl font-semibold">Resumo de comunicados</h1>
        <p class="text-sm text-gray-500 dark:text-gray-400 mt-1">Gerado em lote em {{{esc stamp}}} — não é tempo real.</p>
      </div>
      <a href="/" class="text-sm text-blue-600 dark:text-blue-400 hover:underline">Voltar aos relatórios</a>
    </header>
    {{{body}}}
  </div>
</body>
</html>
"""

let writeToFile (path: string) (generatedAt: DateTime) (ativos: AtivoResumo list) =
    let html = render generatedAt ativos
    File.WriteAllText(path, html)
    path
