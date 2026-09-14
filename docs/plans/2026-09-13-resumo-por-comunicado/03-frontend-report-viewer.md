# Passo 3 — Chevron e popup de resumo em `report_viewer.html`

## Remover o link antigo

No `<header>` (por volta da linha 67), remover:

```html
<a href="/resumos" class="text-sm text-blue-600 dark:text-blue-400 hover:underline">Resumos</a>
```

Essa página estática deixa de existir (passo 0).

## Adicionar o chevron na célula "Tipo de Relatório"

Na função que monta as linhas da tabela (`renderReports`, por volta da linha
372-398), a célula do tipo hoje só tem o link `typeLink`. Adicionar um botão
de chevron logo depois, dentro da mesma `typeCell`:

```js
const typeCell = document.createElement('td');
typeCell.className = 'px-4 py-2 md:whitespace-nowrap text-sm';
const typeLink = document.createElement('a');
typeLink.href = report.url;
typeLink.target = '_blank';
typeLink.rel = 'noopener noreferrer';
typeLink.className = 'text-blue-600 hover:text-blue-800 dark:text-blue-400 dark:hover:text-blue-200 visited:text-purple-600';
typeLink.textContent = report.reportType || 'N/A';
typeCell.appendChild(typeLink);

const summaryBtn = document.createElement('button');
summaryBtn.textContent = '▾';
summaryBtn.title = 'Ver resumo do comunicado';
summaryBtn.setAttribute('aria-label', 'Ver resumo do comunicado');
summaryBtn.className = 'ml-1 text-gray-400 hover:text-blue-600 dark:hover:text-blue-400 focus:outline-none';
summaryBtn.addEventListener('click', () => openSummaryPopup(report.id, report.reportType));
typeCell.appendChild(summaryBtn);

row.appendChild(typeCell);
```

## Markup do popup/modal

Adicionar no final do `<div id="app">` (ou como filho direto do `<body>`,
o que for mais consistente com o resto do arquivo), um modal escondido por
padrão, reaproveitando as classes Tailwind já usadas no restante da página:

```html
<div id="summaryModal" class="hidden fixed inset-0 bg-black/50 flex items-center justify-center p-4 z-50">
  <div class="bg-white dark:bg-gray-800 rounded-xl shadow-lg p-6 max-w-lg w-full space-y-4">
    <div class="flex items-start justify-between gap-4">
      <h3 id="summaryModalTitle" class="text-lg font-semibold text-gray-800 dark:text-gray-100"></h3>
      <button id="summaryModalClose" class="text-gray-400 hover:text-gray-600 dark:hover:text-gray-300" aria-label="Fechar">✕</button>
    </div>
    <div id="summaryModalBody" class="text-sm text-gray-700 dark:text-gray-300 whitespace-pre-line min-h-[3rem]"></div>
  </div>
</div>
```

## Lógica em JavaScript

Adicionar, junto com as outras funções do script:

```js
const summaryCache = {}; // id -> texto do resumo, evita rebuscar na mesma sessão

async function openSummaryPopup(id, title) {
    const modal = document.getElementById('summaryModal');
    const body = document.getElementById('summaryModalBody');
    document.getElementById('summaryModalTitle').textContent = title || 'Resumo';
    modal.classList.remove('hidden');

    if (summaryCache[id]) {
        body.textContent = summaryCache[id];
        return;
    }

    body.innerHTML = '<span class="text-gray-400">Gerando resumo…</span>';
    try {
        const resp = await fetch(`/api/news/${id}/summary`);
        if (!resp.ok) {
            const err = await resp.json().catch(() => ({}));
            body.innerHTML = `<span class="text-red-600 dark:text-red-400">Não foi possível gerar o resumo${err.error ? ': ' + err.error : ''}.</span>
                <button id="summaryRetryBtn" class="block mt-2 text-blue-600 dark:text-blue-400 hover:underline">Tentar de novo</button>`;
            document.getElementById('summaryRetryBtn').addEventListener('click', () => openSummaryPopup(id, title));
            return;
        }
        const data = await resp.json();
        summaryCache[id] = data.summary;
        body.textContent = data.summary;
    } catch (e) {
        body.innerHTML = `<span class="text-red-600 dark:text-red-400">Erro de conexão ao gerar o resumo.</span>
            <button id="summaryRetryBtn" class="block mt-2 text-blue-600 dark:text-blue-400 hover:underline">Tentar de novo</button>`;
        document.getElementById('summaryRetryBtn').addEventListener('click', () => openSummaryPopup(id, title));
    }
}

document.getElementById('summaryModalClose').addEventListener('click', () => {
    document.getElementById('summaryModal').classList.add('hidden');
});
document.getElementById('summaryModal').addEventListener('click', (e) => {
    if (e.target.id === 'summaryModal') e.target.classList.add('hidden');
});
document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') document.getElementById('summaryModal').classList.add('hidden');
});
```

Encaixe esses `addEventListener` no mesmo lugar onde o resto da página já
registra os listeners de inicialização (procure o bloco existente que faz
`document.getElementById('configBtn').addEventListener(...)` ou similar, e
adicione ao lado — não deixe soltos no meio do arquivo).

## Critério de aceite

- Clicar no chevron de um comunicado sem resumo ainda mostra "Gerando
  resumo…", depois substitui pelo texto retornado pela API.
- Clicar de novo no mesmo comunicado (mesma sessão) mostra o resumo
  instantaneamente, sem nova chamada de rede (usa `summaryCache`).
- Recarregar a página e clicar de novo no mesmo comunicado ainda mostra o
  resumo instantaneamente (agora vindo do `reports.json`, via API — a
  primeira chamada já persistiu).
- Uma falha da API mostra mensagem de erro e um botão "Tentar de novo"
  funcional, sem quebrar o resto da página.
- Fechar o popup funciona pelo X, clicando fora, e apertando Esc.
