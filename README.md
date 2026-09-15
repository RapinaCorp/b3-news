# B3News

B3News é uma aplicação .NET/F# projetada para baixar, analisar e exibir notícias e relatórios financeiros da bolsa de valores B3. A ferramenta oferece uma interface de linha de comando para processamento de dados e uma interface web para visualizar os dados coletados.

## Funcionalidades

*   **Coleta de Notícias:** Baixa e processa relatórios e comunicados diretamente do site da B3.
*   **Cache Local:** Armazena os dados em um arquivo JSON local (`reports.json`) para acesso rápido e para evitar downloads repetidos.
*   **Interface Web Interativa:** Páginas web (`report_viewer.html` e `report_viewer_legacy.html` em `/legacy`) para visualizar, filtrar, ordenar e paginar os relatórios.
*   **Resumo por Comunicado:** Cada linha da coluna "Tipo de Relatório" tem um botão chevron (▾) que abre um modal com o resumo daquele comunicado em até 4 linhas, em português (o que foi decidido/anunciado, valores relevantes e impacto para o acionista). O resumo é gerado sob demanda via NVIDIA na primeira vez e depois reaproveitado do cache.
*   **Atualizações em Tempo Real:** A interface web verifica e notifica o usuário sobre novos relatórios disponíveis.
*   **Filtragem Avançada:** Permite filtrar relatórios por ticker, tipo, data e conteúdo do nome do arquivo.

## Como Iniciar

### Pré-requisitos

*   [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Compilando e Executando

1.  **Clone o repositório:**
    ```bash
    git clone <url-do-repositorio>
    cd B3News
    ```

2.  **Compile o projeto:**
    ```bash
    dotnet build
    ```

3.  **Execute a aplicação:**
    Para ver as opções de linha de comando disponíveis, execute:
    ```bash
    dotnet run
    ```

## Como Usar

A aplicação pode ser executada com diferentes argumentos para realizar tarefas específicas:

*   **Atualizar o cache de notícias em JSON:**
    Este comando adiciona ao arquivo `reports.json` as notícias do período informado (`data1` e, opcionalmente, `data2` no formato `AAAA-MM-DD` ou `hoje`):
    ```bash
    dotnet run update-json <data1> [<data2>]
    ```
    Exemplo: `dotnet run update-json hoje`

*   **Iniciar o servidor da API:**
    Este comando inicia um servidor local para servir a interface web e a API JSON de visualização.
    ```bash
    dotnet run server
    ```
    Após iniciar, acesse [http://localhost:8082](http://localhost:8082) no seu navegador. A versão legada (compatível com iOS 9) está em [http://localhost:8082/legacy](http://localhost:8082/legacy).

## Resumo por Comunicado

Cada item em `reports.json` tem um campo `summary` (texto; `""`/nulo = ainda não resumido). Itens antigos sem o campo continuam sendo lidos normalmente.

1.  Na tabela, clique no chevron (▾) ao lado do "Tipo de Relatório" para abrir o modal de resumo.
2.  Na primeira vez, o frontend mostra "Gerando resumo…" e chama `GET /api/news/{id}/summary`. O servidor extrai o texto do comunicado (`ContentExtractor`), gera o resumo via NVIDIA (`NewsSummary.summarizeDetail`) e salva em `reports.json` com `JsonStore.updateSummary`.
3.  Nas próximas vezes o resumo volta na hora, sem chamar a NVIDIA: primeiro do cache em memória da sessão (`summaryCache`), e após recarregar a página, do `reports.json` via API. O modal exibe o texto em bullets por frase e fecha pelo ✕, clicando fora ou com Esc.
4.  Falhas de extração/geração não são persistidas — o campo continua vazio e o modal mostra erro com botão "Tentar de novo". Se a geração funcionar mas o salvamento em disco falhar, a API ainda retorna o resumo, só não fica cacheado.

É necessário exportar `NVIDIA_API_KEY=nvapi-...` para gerar resumos novos. Consultar resumos já cacheados funciona sem a chave.

### Endpoint

*   `GET /api/news/{id}/summary` → `{"summary": "..."}` quando existe ou foi gerado; `400` para ID inválido, `404` para comunicado não encontrado, `500` sem `NVIDIA_API_KEY` ou sem conseguir ler o `reports.json`, `502` quando a extração ou a NVIDIA falha.

## Lógica de Paginação Estável

Para garantir uma experiência de usuário consistente ao navegar pelas páginas de resultados, a aplicação utiliza um mecanismo de "snapshot" que previne que novos dados interfiram na paginação atual.

1.  **Snapshot Inicial:** Quando a página é carregada ou um filtro é aplicado, o frontend solicita os dados ao servidor e armazena o número total de registros (`totalRecords`) recebido. Este número, chamado `clientRecordCount`, representa um "snapshot" do estado do dataset naquele momento.

2.  **Requisições de Paginação:** Ao navegar para outras páginas (ex: "2", "Próxima"), o frontend envia o valor `clientRecordCount` junto com a requisição.

3.  **Processamento no Servidor:** O servidor recebe este valor e, antes de ordenar e paginar os resultados, ele **trunca** o conjunto de dados para que contenha exatamente o número de registros que o cliente conhece. Isso "congela" a visão do cliente sobre os dados, ignorando quaisquer relatórios que tenham chegado após o snapshot inicial.

4.  **Notificação de Atualização:** O usuário é notificado sobre novos relatórios através de um ícone de "caixa de correio" na interface. Ao clicar neste ícone, uma nova requisição é feita para obter os dados mais recentes, um novo snapshot é criado e a contagem de registros do cliente é atualizada.

Este mecanismo garante que a ordem e o conteúdo das páginas permaneçam estáveis durante a navegação, evitando que um item da página 2 apareça de repente na página 1.

## Configuração

A aplicação utiliza um arquivo `b3news.settings.json` para configurações. As principais configurações são a lista de `Tickers` para filtrar as notícias durante o download e o bloco `Summary` para geração de resumos. O resumo é gerado um por vez, por requisição HTTP — não há processamento em lote/paralelo.

É preciso definir a variável de ambiente `NVIDIA_API_KEY=nvapi-...` para gerar resumos novos.

**Exemplo de `b3news.settings.json`:**
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://localhost:8082"
      }
    }
  },
  "Tickers": [
    "ABEV",
    "PETR",
    "VALE"
  ],
  "Summary": {
    "Model": "nvidia/nemotron-3-super-120b-a12b",
    "MaxChars": 7000,
    "MaxRetries": 7,
    "DisableReasoning": true
  }
}
```

*   `Model`: modelo da NVIDIA usado em `NewsSummary.summarizeDetail`.
*   `MaxChars`: limite de caracteres do conteúdo extraído enviado ao modelo.
*   `MaxRetries`: tentativas em caso de erro transitório/rate limit.
*   `DisableReasoning`: prefixa o prompt com `/no_think` (só faz efeito em modelos Nemotron; outros modelos ignoram como texto comum).

## Compilando para Outras Plataformas

É possível compilar a aplicação como um executável auto-contido para outras plataformas, como o Linux:

```bash
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
```

## REST tests

* GET https://sistemasweb.b3.com.br/PlantaoNoticias/Noticias/ListarTitulosNoticias?agencia=18&dataInicial=2025-06-04&datafinal=2025-06-04
* GET https://sistemasweb.b3.com.br/PlantaoNoticias/Noticias/Detail?idNoticia=2960041&agencia=18&dataNoticia=2025-06-04
* GET http://localhost:8082/api/news?Codes=PETR,VALE&PageSize=5&Page=2&filename=mercado
* GET http://localhost:8082/api/news?Codes=PETR,VALE
