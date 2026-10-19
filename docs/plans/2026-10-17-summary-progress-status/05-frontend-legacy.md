# Passo 5 — Polling ES5 no `report_viewer_legacy.html`

Restrição de projeto: o legacy precisa continuar iOS 9-safe — **sem**
`fetch`, `Promise`, `classList`, arrow functions ou `const`/`let`. Usar
`XMLHttpRequest`, `setTimeout` e `var`.

## Constantes

No objeto `constants` (linhas ~328-333), adicionar:

```js
SUMMARY_POLL_INTERVAL_MS: 2000,
SUMMARY_POLL_MAX_ATTEMPTS: 900
```

## CSS novo

Junto das regras de `.summary-btn` (~linha 225):

```css
.summary-status {
    color: #888;
}
html.dark .summary-status {
    color: #aaa;
}
```

## Substituir o bloco do popup (linhas ~499-600)

```js
var summaryCache = {}; // id -> texto do resumo, evita rebuscar na mesma sessão
var summaryPollToken = 0;
var currentSummaryXhr = null;

function beginSummaryPoll(id) {
    summaryPollToken += 1;
    return { token: summaryPollToken, id: String(id) };
}

// Um poll só pode renderizar se ainda é o pedido mais recente, do mesmo id,
// com o modal aberto.
function isCurrentSummaryPoll(poll) {
    var body = document.getElementById('summaryModalBody');
    var modal = document.getElementById('summaryModal');
    return poll.token === summaryPollToken &&
        body.getAttribute('data-summary-id') === poll.id &&
        modal.style.display === 'block';
}

function renderSummaryStatus(body, message) {
    body.innerHTML = '';
    var span = document.createElement('span');
    span.className = 'summary-status';
    span.textContent = message;
    body.appendChild(span);
}

// ... splitSummarySentences e renderSummaryBullets permanecem iguais ...

function showSummaryModal() {
    document.getElementById('summaryModal').style.display = 'block';
}

function hideSummaryModal() {
    // Invalida qualquer poll em voo e aborta o XHR corrente.
    summaryPollToken += 1;
    if (currentSummaryXhr) {
        try {
            currentSummaryXhr.abort();
        } catch (abortErr) {}
        currentSummaryXhr = null;
    }
    document.getElementById('summaryModal').style.display = 'none';
}

function showSummaryError(message, id, title) {
    var body = document.getElementById('summaryModalBody');
    body.innerHTML = '';
    var msg = document.createElement('span');
    msg.className = 'summary-error';
    msg.textContent = message;
    var retry = document.createElement('button');
    retry.textContent = 'Tentar de novo';
    retry.style.display = 'block';
    retry.style.marginTop = '8px';
    retry.onclick = function () { openSummaryPopup(id, title, null); };
    body.appendChild(msg);
    body.appendChild(retry);
}

function pollSummary(id, title, poll, attempt) {
    if (!isCurrentSummaryPoll(poll)) return;

    var body = document.getElementById('summaryModalBody');
    var xhr = new XMLHttpRequest();
    currentSummaryXhr = xhr;
    xhr.open('GET', '/api/news/' + id + '/summary', true);
    xhr.onreadystatechange = function () {
        if (xhr.readyState !== 4) return;
        if (currentSummaryXhr === xhr) currentSummaryXhr = null;
        if (!isCurrentSummaryPoll(poll)) return;

        if (xhr.status === 200) {
            var data = JSON.parse(xhr.responseText);
            if (data.summary) {
                summaryCache[id] = data.summary;
                renderSummaryBullets(body, data.summary);
                return;
            }
            renderSummaryStatus(body, data.message || data.status || 'Gerando resumo…');
            if (attempt + 1 < constants.SUMMARY_POLL_MAX_ATTEMPTS) {
                setTimeout(function () {
                    pollSummary(id, title, poll, attempt + 1);
                }, constants.SUMMARY_POLL_INTERVAL_MS);
            } else {
                showSummaryError('Tempo esgotado gerando o resumo.', id, title);
            }
        } else {
            var errMsg = 'Falha ao gerar o resumo.';
            try {
                var err = JSON.parse(xhr.responseText);
                if (err.error) errMsg = 'Não foi possível gerar o resumo: ' + err.error;
            } catch (parseErr) {}
            showSummaryError(errMsg, id, title);
        }
    };
    xhr.send();
}

function openSummaryPopup(id, title, summaryText) {
    var body = document.getElementById('summaryModalBody');
    document.getElementById('summaryModalTitle').textContent = title || 'Resumo';
    body.setAttribute('data-summary-id', String(id));
    showSummaryModal();

    if (summaryText || summaryCache[id]) {
        renderSummaryBullets(body, summaryText || summaryCache[id]);
        return;
    }

    renderSummaryStatus(body, 'Gerando resumo…');
    pollSummary(id, title, beginSummaryPoll(id), 0);
}
```

Notas:

- O token é criado **uma vez** por abertura de popup (`beginSummaryPoll` no
  `openSummaryPopup`) e passado adiante; os `setTimeout` reutilizam o mesmo
  objeto `poll` — assim um popup novo invalida a cadeia inteira.
- `hideSummaryModal` já é chamado pelo X, pelo clique no overlay e pelo Esc
  (linhas ~753-762); não é preciso mexer nesses listeners.
- O retry de `showSummaryError` reusa o `openSummaryPopup` existente.

## Critério de aceite

- Mesmo comportamento do viewer moderno em navegador atual.
- Nenhuma construção ES6+ introduzida (checar por `fetch(`, `=>`,
  `classList`, `Promise`, `let `/`const `).
- Fechar o modal aborta o XHR pendente e nenhuma continuation atrasada
  renderiza em um popup novo.

## Ajustes pós-revisão

- `xhr.status === 0` (sem rede) mostra a mesma mensagem do viewer moderno
  ("Erro de conexão ao gerar o resumo.") em vez de "Falha ao gerar o resumo.".
- `JSON.parse` do corpo 200 ganhou `try/catch` → `showSummaryError('Erro ao
  interpretar a resposta do servidor.')`; um corpo inválido travava o modal
  sem retry.
