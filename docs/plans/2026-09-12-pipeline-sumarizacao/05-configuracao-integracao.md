# Passo 5 — Configuração, seleção de ativos e integração no projeto

## Objetivo

Amarrar os módulos dos passos 1-4 na estrutura existente do b3-news: config,
seleção de ativos, execução em lote (job/CLI/agendamento), e a variável de
ambiente da chave da NVIDIA.

## Requisitos

### Configuração

- Adicionar ao sistema de config já existente do projeto (arquivo de config,
  variáveis de ambiente, etc — seguir o padrão atual):
  - `NVIDIA_API_KEY` (obrigatório, não versionar/logar o valor)
  - Nome do modelo (padrão: `meta/llama-3.3-70b-instruct`, sobrescrevível)
  - Número de últimos comunicados por ativo a considerar (padrão sugerido: 5)
  - Limite de caracteres por documento extraído (padrão sugerido: 6000-8000)
  - Grau máximo de paralelismo nas chamadas à NVIDIA (padrão sugerido: 5-8,
    respeitando o limite de ~40 req/min)

### Seleção de ativos

- Definir de onde vem a "lista de ativos selecionados" a processar: reusar
  lista/config já existente no projeto, se houver, ou criar uma lista simples
  (arquivo de config ou lista hardcoded inicial) — perguntar ao usuário
  (Adriano) qual é a fonte correta antes de assumir, se não estiver claro no
  código.

### Execução

- Adicionar um comando/rotina (CLI existente do projeto, ou novo comando)
  que execute o pipeline completo (passos 1-4) para a lista de ativos
  configurada e gere/atualize a página.
- Não precisa ser tempo real: pode ser um job batch (ex: rodado manualmente
  ou agendado via cron/systemd timer, seguindo o que já existe no ambiente
  de Adriano em dude333.com).

### Testes

- Fazer ao menos uma execução ponta a ponta com dados reais (ativos reais,
  comunicados reais) e validar a página gerada.
- Verificar comportamento com `NVIDIA_API_KEY` ausente ou inválida (deve
  falhar com mensagem clara, não travar silenciosamente).

## Critério de aceite final da feature

- Executar o comando/rotina gera a página com os ativos selecionados e seus
  resumos, sem intervenção manual além de disparar a execução.
- Chave da NVIDIA e demais parâmetros são configuráveis sem alterar código.
- Uma falha em um comunicado ou ativo específico não impede a geração da
  página com os demais.
