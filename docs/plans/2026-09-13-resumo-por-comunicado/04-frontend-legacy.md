# Passo 4 — Chevron e popup de resumo em `report_viewer_legacy.html`

## Objetivo

Mesmo comportamento do passo 3, adaptado ao estilo do arquivo legado (CSS
próprio, sem Tailwind, sem link "Resumos" para remover — este arquivo nunca
teve esse link).

## Adicionar o chevron na célula "Tipo de Relatório"

Na função `renderTable` (por volta da linha 339-345):

```js
var typeCell = document.createElement('td');
var typeLink = document.createElement('a');
typeLink.href = report.url;
typeLink.target = '_blank';
typeLink.textContent = report.reportType || 'N/A';
typeCell.appendChild(typeLink);

var summaryBtn = document.createElement('button');
summaryBtn.textContent = '▾';
summaryBtn.title = 'Ver resumo do comunicado';
summaryBtn.className = 'summary-btn';
summaryBtn.onclick = (function (id, title) {
    return function () { openSummaryPopup(id, title); };
})(report.id, report.reportType);
typeCell.appendChild(summaryBtn);

row.appendChild(typeCell);
```

## CSS

Adicionar junto ao restante do `<style>` do arquivo, seguindo a mesma
convenção simples já usada (ex: a classe `.hide` por volta da linha 145):

```css
.summary-btn {
    margin-left: 4px;
    background: none;
    border: none;
    cursor: pointer;
    color: #888;
}
.summary-btn:hover {
    color: #06c;
}
#summaryModal {
    position: fixed;
    inset: 0;
    background: rgba(0,0,0,0.5);
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 16px;
    z-index: 50;
}
#summaryModal .box {
    background: #fff;
    border-radius: 8px;
    box-shadow: 0 4px 16px rgba(0,0,0,0.2);
    padding: 20px;
    max-width: 480px;
    width: 100%;
}
#summaryModalBody {
    white-space: pre-line;
    font-size: 14px;
    min-height: 3rem;
    margin-top: 12px;
}
```

Reaproveite a classe `.hide` já existente no arquivo para esconder/mostrar o
modal, em vez de introduzir uma segunda convenção (`hidden`/`display:none`
manual).

## Markup do modal

Adicionar próximo ao final do `<div id="app">`:

```html
<div id="summaryModal" class="hide">
  <div class="box">
    <div style="display:flex; justify-content:space-between; align-items:flex-start;">
      <h3 id="summaryModalTitle"></h3>
      <button id="summaryModalClose" aria-label="Fechar">✕</button>
    </div>
    <div id="summaryModalBody"></div>
  </div>
</div>
```

## Lógica em JavaScript

```js
var summaryCache = {};

function openSummaryPopup(id, title) {
    var modal = document.getElementById('summaryModal');
    var body = document.getElementById('summaryModalBody');
    document.getElementById('summaryModalTitle').textContent = title || 'Resumo';
    modal.classList.remove('hide');

    if (summaryCache[id]) {
        body.textContent = summaryCache[id];
        return;
    }

    body.textContent = 'Gerando resumo…';
    fetch('/api/news/' + id + '/summary')
        .then(function (resp) {
            if (!resp.ok) {
                return resp.json().catch(function () { return {}; }).then(function (err) {
                    throw new Error(err.error || 'Falha ao gerar o resumo.');
                });
            }
            return resp.json();
        })
        .then(function (data) {
            summaryCache[id] = data.summary;
            body.textContent = data.summary;
        })
        .catch(function (e) {
            body.innerHTML = '';
            var msg = document.createElement('span');
            msg.style.color = '#c00';
            msg.textContent = e.message || 'Erro ao gerar o resumo.';
            var retry = document.createElement('button');
            retry.textContent = 'Tentar de novo';
            retry.style.display = 'block';
            retry.style.marginTop = '8px';
            retry.onclick = function () { openSummaryPopup(id, title); };
            body.appendChild(msg);
            body.appendChild(retry);
        });
}

document.getElementById('summaryModalClose').addEventListener('click', function () {
    document.getElementById('summaryModal').classList.add('hide');
});
document.getElementById('summaryModal').addEventListener('click', function (e) {
    if (e.target.id === 'summaryModal') e.target.classList.add('hide');
});
document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') document.getElementById('summaryModal').classList.add('hide');
});
```

Encaixe os `addEventListener` de inicialização junto com o restante do
código de setup do arquivo (perto de onde os outros listeners de botões já
são registrados), não soltos no meio do arquivo.

## Critério de aceite

- Mesmos critérios do passo 3 (`03-frontend-report-viewer.md`), validados
  aqui em `report_viewer_legacy.html`: gerar, cachear em memória, persistir
  via API, mostrar erro com retry, fechar por X/clique fora/Esc.
