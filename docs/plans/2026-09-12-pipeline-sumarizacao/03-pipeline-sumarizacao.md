# Passo 3 — Pipeline de sumarização (por documento e por ativo)

## Objetivo

Criar um módulo (ex: `SummaryPipeline.fs`) que orquestra: para cada ativo
selecionado, buscar os últimos N comunicados, extrair o texto de cada um
(passo 2), resumir cada um individualmente (passo 1) e depois gerar um
resumo consolidado do ativo a partir dos resumos individuais.

## Requisitos

### Resumo individual por comunicado

Prompt de sistema sugerido (ajustar se necessário):

```
Resuma o comunicado abaixo em até 4 linhas, focando em:
o que foi decidido/anunciado, valores relevantes, e impacto pro acionista.
Não repita o texto, seja direto.
```

- Entrada: texto extraído do comunicado (passo 2, já truncado).
- Saída: resumo curto (string).
- Processar comunicados de ativos diferentes em paralelo é aceitável, mas
  respeitar o rate limit da NVIDIA (passo 1 já trata 429 com retry — ainda
  assim, limitar o grau de paralelismo, ex: `Async.Parallel` com um máximo de
  N chamadas simultâneas, para não estourar o limite de 40 req/min).

### Resumo consolidado por ativo

Prompt de sistema sugerido:

```
Você recebeu resumos dos últimos comunicados da empresa {ativo}.
Produza um resumo executivo dos principais pontos, em ordem cronológica,
destacando o que é mais relevante para decisão de investimento.
```

- Entrada: os resumos individuais do ativo, concatenados com a data de cada
  comunicado.
- Saída: um resumo consolidado (string) por ativo.

## Tipo de dado de saída do pipeline

```fsharp
type ComunicadoResumo = { Data: DateTime; Titulo: string; Url: string; Resumo: string }
type AtivoResumo = { Ativo: string; Comunicados: ComunicadoResumo list; ResumoConsolidado: string }
```

Ajustar nomes/tipos conforme convenções já usadas no projeto para representar
ativos e documentos.

## Tratamento de falhas parciais

- Se a extração ou o resumo de um comunicado específico falhar, o ativo ainda
  deve aparecer no resultado final com os comunicados que funcionaram (e,
  opcionalmente, uma nota indicando que N comunicados não puderam ser
  processados).
- Logar falhas para diagnóstico, sem interromper o pipeline.

## Critério de aceite

- Rodar o pipeline para 2-3 ativos reais, com pelo menos um PDF e uma página
  HTML entre os comunicados, e obter um `AtivoResumo` coerente para cada.
- Uma falha isolada (ex: um link fora do ar) não derruba o processamento dos
  outros ativos/comunicados.
