# Passo 4 — Polling no `report_viewer.html`

## Constantes e estado

Em `constants` (linhas ~187-197), adicionar:

```js
SUMMARY_POLL_INTERVAL_MS: 2000,
SUMMARY_POLL_MAX_ATTEMPTS: 900, // 30 min (pior caso do pipeline ≈ 13 min)
```

Em `state` (linhas ~200-222), adicionar o token monotônico:

```js
summaryRequestToken: 0,
```

## Helpers novos (junto do bloco `// --- SUMMARY POPUP ---`)

```js
function delay(ms) {
    return new Promise(resolve => setTimeout(resolve, ms));
}

function beginSummaryRequest(id) {
    state.summaryRequestToken += 1;
    return { token: state.summaryRequestToken, id: String(id) };
}

function cancelSummaryRequest() {
    state.summaryRequestToken += 1;
}

// Um poll só pode renderizar se ainda é o pedido mais recente, do mesmo id,
// com o modal aberto — evita continuations atrasadas sobrescreverem um popup
// mais novo.
function isCurrentSummaryRequest(req) {
    const modal = document.getElementById('summaryModal');
    const body = document.getElementById('summaryModalBody');
    return req.token === state.summaryRequestToken &&
        body.dataset.summaryId === req.id &&
        !modal.classList.contains('hidden');
}

function renderSummaryStatus(body, message) {
    body.innerHTML = '';
    const span = document.createElement('span');
    span.className = 'text-gray-400';
    span.textContent = message;
    body.appendChild(span);
}
```

## Substituir `openSummaryPopup` (linhas ~442-472)

```js
async function pollSummary(id) {
    const req = beginSummaryRequest(id);

    for (let attempt = 0; attempt < constants.SUMMARY_POLL_MAX_ATTEMPTS; attempt++) {
        if (!isCurrentSummaryRequest(req)) return false;

        let resp;
        try {
            resp = await fetch(`/api/news/${id}/summary`);
        } catch (e) {
            if (isCurrentSummaryRequest(req)) showSummaryError('Erro de conexão ao gerar o resumo.');
            return false;
        }
        if (!isCurrentSummaryRequest(req)) return false;

        if (!resp.ok) {
            const err = await resp.json().catch(() => ({}));
            if (isCurrentSummaryRequest(req)) {
                showSummaryError(`Não foi possível gerar o resumo${err.error ? ': ' + err.error : ''}.`);
            }
            return false;
        }

        const data = await resp.json();
        if (!isCurrentSummaryRequest(req)) return false;

        if (data.summary) {
            summaryCache[id] = data.summary;
            renderSummaryBullets(document.getElementById('summaryModalBody'), data.summary);
            return true;
        }

        renderSummaryStatus(
            document.getElementById('summaryModalBody'),
            data.message || data.status || 'Gerando resumo…'
        );

        if (attempt + 1 < constants.SUMMARY_POLL_MAX_ATTEMPTS) {
            await delay(constants.SUMMARY_POLL_INTERVAL_MS);
        }
    }

    if (isCurrentSummaryRequest(req)) showSummaryError('Tempo esgotado gerando o resumo.');
    return false;
}

async function openSummaryPopup(id, title, summaryText) {
    const modal = document.getElementById('summaryModal');
    const body = document.getElementById('summaryModalBody');
    document.getElementById('summaryModalTitle').textContent = title || 'Resumo';
    body.dataset.summaryId = id;
    body.dataset.summaryTitle = title || 'Resumo';
    modal.classList.remove('hidden');

    if (summaryText || summaryCache[id]) {
        renderSummaryBullets(body, summaryText || summaryCache[id]);
        return true;
    }

    renderSummaryStatus(body, 'Gerando resumo…');
    return await pollSummary(id);
}
```

O botão de retry de `showSummaryError` continua o mesmo (re-chama
`openSummaryPopup` com `body.dataset.summaryId`); o novo `beginSummaryRequest`
invalida qualquer poll antigo.

## Cancelar ao fechar (linhas ~811-819)

```js
document.getElementById('summaryModalClose').addEventListener('click', () => {
    cancelSummaryRequest();
    document.getElementById('summaryModal').classList.add('hidden');
});
document.getElementById('summaryModal').addEventListener('click', (e) => {
    if (e.target.id === 'summaryModal') {
        cancelSummaryRequest();
        e.target.classList.add('hidden');
    }
});
document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') {
        cancelSummaryRequest();
        document.getElementById('summaryModal').classList.add('hidden');
    }
});
```

O handler de clique do botão ▾ em `renderTable` não muda: ele já espera
`openSummaryPopup` resolver `true` e então pinta o botão de verde usando
`summaryCache[report.id] || report.summary`.

## Critério de aceite

- Item sem resumo: modal mostra "Gerando resumo…" → mensagens de etapa →
  resumo em bullets.
- Item cacheado: resumo aparece imediatamente, sem rede.
- Fechar o modal e reabrir outro id rapidamente não deixa o poll antigo
  sobrescrever o novo popup.
- Erro/timeout mostram mensagem + "Tentar de novo" funcional.
- Após `dotnet build`, o HTML é recurso embutido — precisa rebuild para servir.

## Ajustes pós-revisão

- `resp.json()` do 200 ganhou `.catch(() => null)` + `showSummaryError('Erro
  ao interpretar a resposta do servidor.')` — um corpo 200 não-JSON rejeitava
  `pollSummary` e deixava o modal sem "Tentar de novo".
- O retry de `showSummaryError` passou a aguardar `openSummaryPopup` e chamar
  `markSummaryLoaded(id, state.lastClickedSummaryBtn)`; o bloco de atualização
  de linha do clique em ▾ virou o helper `markSummaryLoaded`, usado pelos dois
  caminhos (o retry antes deixava o chevron laranja após sucesso).
