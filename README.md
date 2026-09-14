# B3News

B3News é uma aplicação .NET/F# projetada para baixar, analisar e exibir notícias e relatórios financeiros da bolsa de valores B3. A ferramenta oferece uma interface de linha de comando para processamento de dados e uma interface web para visualizar os dados coletados.

## Funcionalidades

*   **Coleta de Notícias:** Baixa e processa relatórios e comunicados diretamente do site da B3.
*   **Cache Local:** Armazena os dados em um arquivo JSON local (`reports.json`) para acesso rápido e para evitar downloads repetidos.
*   **Interface Web Interativa:** Uma página web (`report_viewer.html`) para visualizar, filtrar, ordenar e paginar os relatórios.
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
    dotnet run serve-api
    ```
    Após iniciar, acesse [http://localhost:8082](http://localhost:8082) no seu navegador.

## Lógica de Paginação Estável

Para garantir uma experiência de usuário consistente ao navegar pelas páginas de resultados, a aplicação utiliza um mecanismo de "snapshot" que previne que novos dados interfiram na paginação atual.

1.  **Snapshot Inicial:** Quando a página é carregada ou um filtro é aplicado, o frontend solicita os dados ao servidor e armazena o número total de registros (`totalRecords`) recebido. Este número, chamado `clientRecordCount`, representa um "snapshot" do estado do dataset naquele momento.

2.  **Requisições de Paginação:** Ao navegar para outras páginas (ex: "2", "Próxima"), o frontend envia o valor `clientRecordCount` junto com a requisição.

3.  **Processamento no Servidor:** O servidor recebe este valor e, antes de ordenar e paginar os resultados, ele **trunca** o conjunto de dados para que contenha exatamente o número de registros que o cliente conhece. Isso "congela" a visão do cliente sobre os dados, ignorando quaisquer relatórios que tenham chegado após o snapshot inicial.

4.  **Notificação de Atualização:** O usuário é notificado sobre novos relatórios através de um ícone de "caixa de correio" na interface. Ao clicar neste ícone, uma nova requisição é feita para obter os dados mais recentes, um novo snapshot é criado e a contagem de registros do cliente é atualizada.

Este mecanismo garante que a ordem e o conteúdo das páginas permaneçam estáveis durante a navegação, evitando que um item da página 2 apareça de repente na página 1.

## Configuração

A aplicação utiliza um arquivo `b3news.settings.json` para configurações. A principal configuração que você pode querer alterar é a lista de `Tickers` para filtrar as notícias durante o download.

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
  ]
}
```

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
