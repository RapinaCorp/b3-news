# Passo 0 — Visão geral da feature: Resumo de comunicados por ativo

## Objetivo

Adicionar ao projeto **b3-news** (F#, https://github.com/RapinaCorp/b3-news) uma nova
funcionalidade: gerar uma página com uma seleção de ativos e, para cada ativo, um
resumo dos últimos comunicados (documentos IPE/Enet e Fnet), usando um LLM gratuito
da NVIDIA (build.nvidia.com) para sumarizar o conteúdo.

Antes de escrever qualquer código, explore a estrutura atual do repositório
(especialmente o módulo `Download.fs` e como ele já baixa/organiza documentos do
Fnet e Enet) para entender convenções de nomes, formato de dados e onde encaixar
os novos módulos. Siga o estilo de código já existente no projeto (uso de
`Thoth.Json.Net`, tratamento de erros, etc).

## Arquitetura da feature (alto nível)

1. **Seleção de ativos**: lista configurável de ativos a processar (reaproveitar
   o que já existe no projeto para listar/filtrar ativos, se houver).
2. **Coleta dos links**: para cada ativo, obter os últimos N comunicados
   (usando a lógica de download já existente do Fnet/Enet).
3. **Extração de texto**: cada link aponta para uma página web (HTML) ou um PDF.
   Extrair o texto principal de cada um.
4. **Sumarização via NVIDIA**: enviar o texto extraído para a API da NVIDIA e
   obter um resumo curto por comunicado; depois, um resumo consolidado por ativo.
5. **Geração da página**: montar uma página (seguir o formato de saída que o
   projeto já usa — HTML estático, ou o que for convenção do repo) com a lista
   de ativos selecionados e seus resumos.

## Decisões já tomadas (não repetir a discussão, só implementar)

- **Provedor de LLM**: NVIDIA NIM, endpoint OpenAI-compatible em
  `https://integrate.api.nvidia.com/v1/chat/completions`.
- **Autenticação**: header `Authorization: Bearer $NVIDIA_API_KEY`
  (variável de ambiente `NVIDIA_API_KEY`, chave no formato `nvapi-...`).
- **Modelo padrão**: `meta/llama-3.3-70b-instruct` (configurável, ver passo 5).
- **Rate limit**: tier gratuito tem ~40 requisições/minuto por conta — o
  pipeline deve rodar em lote/batch (não precisa ser tempo real) e deve
  respeitar isso (throttling/retry com backoff).
- **Estratégia de sumarização**: resumo individual por comunicado (curto,
  3-5 linhas) e depois um resumo agregado por ativo a partir dos resumos
  individuais — não concatenar todos os documentos brutos numa única chamada.
- **Extração de conteúdo**: truncar cada documento extraído a um tamanho
  razoável (ex: 6-8k caracteres) antes de enviar ao modelo.

## Ordem de implementação

Os próximos arquivos (`01` a `05`) descrevem cada etapa em detalhe, na ordem
em que devem ser implementadas. Cada etapa deve compilar e ter um teste manual
ou automatizado antes de seguir para a próxima.

1. `01-cliente-nvidia.md` — cliente HTTP para a API da NVIDIA
2. `02-extracao-conteudo.md` — extração de texto de HTML e PDF
3. `03-pipeline-sumarizacao.md` — orquestração do resumo por documento e por ativo
4. `04-geracao-pagina.md` — geração da página final
5. `05-configuracao-integracao.md` — configuração, seleção de ativos, wiring no CLI/app existente
