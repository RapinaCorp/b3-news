# Passo 1 — Cliente HTTP para a API da NVIDIA

## Objetivo

Criar um módulo F# (ex: `NvidiaClient.fs`, ajuste o nome conforme convenção do
projeto) responsável por chamar a API de chat completions da NVIDIA e retornar
o texto gerado.

## Requisitos

- Endpoint: `POST https://integrate.api.nvidia.com/v1/chat/completions`
- Header: `Authorization: Bearer <NVIDIA_API_KEY>`, `Content-Type: application/json`
- Corpo da requisição (formato OpenAI-compatible):
  ```json
  {
    "model": "meta/llama-3.3-70b-instruct",
    "messages": [
      { "role": "system", "content": "<prompt de sistema>" },
      { "role": "user", "content": "<texto a resumir>" }
    ],
    "temperature": 0.3,
    "max_tokens": 512
  }
  ```
- Resposta: extrair `choices[0].message.content` (usar `Thoth.Json.Net` para
  parsear, seguindo o padrão já usado em `Download.fs`).
- A chave da API (`NVIDIA_API_KEY`) e o nome do modelo devem vir de configuração
  (variável de ambiente e/ou arquivo de config do projeto — ver passo 5, não
  hardcode).

## Tratamento de erros e rate limit

- Tratar HTTP 429 (rate limit) com retry e backoff exponencial (ex: esperar
  1s, 2s, 4s, até um máximo de tentativas configurável).
- Tratar erros de rede/timeout de forma que uma falha em um comunicado não
  derrube o processamento dos demais (retornar `Result<string, string>` ou
  tipo de erro equivalente ao padrão do projeto).
- Logar (no nível de log já usado no projeto) cada chamada: ativo, tamanho do
  texto enviado, sucesso/falha, tempo de resposta.

## Assinatura sugerida da função pública

```fsharp
/// Envia um texto para o modelo e retorna o resumo gerado.
val summarize : apiKey: string -> model: string -> systemPrompt: string -> content: string -> Async<Result<string, string>>
```

Ajuste tipos/assinatura para o estilo idiomático já usado no restante do
projeto (ex: se o projeto usa `Task` ao invés de `Async`, ou tipos de erro
próprios).

## Critério de aceite

- Função testável isoladamente (teste manual via REPL/script ou teste
  automatizado) enviando um texto curto de exemplo e recebendo um resumo.
- Rate limit (429) tratado sem quebrar o processo chamador.
- Nenhuma chave de API hardcoded no código.
