## Fluxo

1. Carregar lista com o id dos relatórios (filtrado pelas datas e tickers)
   1. https://sistemasweb.b3.com.br/PlantaoNoticias/Noticias/ListarTitulosNoticias
      * agencia=18
      * dataInicial=_
      * dataFinal=_
   2. Resposta: 
   ```[
        {
          "NwsMsg": {
            "IdAgencia": 18,
            "content": null,
            "dateTime": "2025-06-04 19:58:29",
            "headline": "ISEC (IMWL) CRI E:285 S:1(s) ANUNCIO INICIO - 04/06/2025",
            "id": 2960042
          }
        },
        {
          "NwsMsg": {
            "IdAgencia": 18,
            "content": null,
            "dateTime": "2025-06-04 19:57:27",
            "headline": "AZZAS 2154 (AZZA-NM) - VM Posicao Individual (Cia,Controladas e Coligadas)",
            "id": 2960041
         },...
      ]
      ```
2. Extraír os detalhes de cada um dos relatórios (Details.fetchDetails)
    1. https://sistemasweb.b3.com.br/PlantaoNoticias/Noticias/Detail
      * idNoticia
      * agencia
      * dataNoticia
    2. Resposta: arquivo html

3. Do arquivo html, busca-se por 2 padrões no conteúdo:
    1. let patternFnet = "https://fnet.bmfbovespa.com.br/fnet/publico/visualizarDocumento\\?id=([0-9]+)"

    2. let patternEnet = "https://www.rad.cvm.gov.br/ENET/frmExibirArquivoIPEExterno.aspx\\?ID=([0-9]+)"

4. Com este último id:
    1. Arquivo FNET: https://fnet.bmfbovespa.com.br/fnet/publico/exibirDocumento?id=<id>
    2. Arqvuivo ENET (retorna um pdf em base64): https://www.rad.cvm.gov.br/ENET/frmExibirArquivoIPEExterno.aspx/ExibirPDF"
        * POST
        * { "codigoInstituicao": "2", "numeroProtocolo": "{nid}", "token": "", "versaoCaptcha": "" }


## Arquivo de configuração

### b3news.settings.json

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
    "VALE",
  ]
}
```

## Compilando do Windows para Linux

`dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true`

## Roadmap

* Armazenar lista em um banco de dados (sqlite ou b-tree)
  * [simple-pagination.js](https://gist.github.com/kottenator/9d936eb3e4e3c3e02598)
  * [Persisting data report](https://chatgpt.com/share/6851e67e-a5b0-800d-bbfd-c9307e46b3a6)
  * [Append unique data to JSON](https://chatgpt.com/share/6851e862-1888-800d-8a4c-ab93ed6ef6ce)


## REST tests

GET https://dummyjson.com/c/78db-003d-4189-a446

GET https://sistemasweb.b3.com.br/PlantaoNoticias/Noticias/ListarTitulosNoticias?agencia=18&dataInicial=2025-06-04&datafinal=2025-06-04

GET https://sistemasweb.b3.com.br/PlantaoNoticias/Noticias/Detail?idNoticia=2960041&agencia=18&dataNoticia=2025-06-04

GET http://localhost:8082/api/news?Codes=PETR,VALE&PageSize=5&Page=2&filename=mercado
GET http://localhost:8082/api/news?Codes=PETR,VALE