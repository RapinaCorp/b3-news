# Passo 4 — Geração da página de resumos

## Objetivo

A partir da lista de `AtivoResumo` (passo 3), gerar a página final com a
seleção de ativos e seus resumos, publicada em b3news.dude333.com.

## Requisitos

- Antes de implementar, verificar como o b3-news já gera/serve as páginas
  atuais do site (template engine, geração estática, framework web, etc) e
  seguir o mesmo padrão — não introduzir uma tecnologia de templating nova
  se o projeto já tem uma.
- Estrutura de conteúdo da página, por ativo:
  - Nome/ticker do ativo
  - Resumo consolidado (destaque)
  - Lista dos comunicados individuais usados (data, título, link original,
    resumo curto) — permitir que o leitor abra o documento original
- Indicar de forma visível a data/hora em que o resumo foi gerado (os
  resumos são gerados em lote, não em tempo real).

## Critério de aceite

- Página renderiza corretamente para os ativos de teste do passo 3, com o
  resumo consolidado em destaque e os comunicados individuais listados abaixo.
- Links para os comunicados originais funcionam (apontam para o PDF/página
  real, não para o texto extraído).
