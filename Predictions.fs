module Predicitions

open Microsoft.ML
open Microsoft.ML.Data
open System.Reflection

type TextData =
    { [<LoadColumn(0)>]
      Label: string
      [<LoadColumn(1)>]
      Filename: string }

[<CLIMutable>]
type Prediction =
    { [<ColumnName("PredictedLabel")>]
      Label: string }

let convert term =
    let conversionTable =
        dict
            [ "age", "AGE"
              "aviso", "AVISOS"
              "dfp", "DFP"
              "distrib", "RENDIMENTOS"
              "eventos", "EVENTOS"
              "fr", "FATO_RELEVANTE"
              "mensal", "RELAT_MENSAL"
              "outros", "OUTROS"
              "pos", "POSIÇÕES"
              "ppt", "APRESENTAÇÕES"
              "qa", "QUESTIONAMENTOS"
              "reg", "REGIMENTO"
              "relat", "RELATÓRIOS_GERENCIAIS"
              "rentab", "RENTABILIDADE"
              "reuniao", "REUNIÕES"
              "trimestral", "RELAT_TRIMESTRAL" ]

    conversionTable.[term]

let listResources () =
    printfn "%A" (Assembly.GetExecutingAssembly().GetManifestResourceNames())

let setPrediction =
    use stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("B3News.ML.data.B3NewsModel.zip")
    let context = new MLContext()
    let loadedModel, _ = context.Model.Load(stream)
    context.Model.CreatePredictionEngine<TextData, Prediction>(loadedModel)

let predictFolder (predictionEngine: PredictionEngine<TextData, Prediction>) filename =
    let data: TextData = { Label = ""; Filename = filename }
    predictionEngine.Predict(data).Label |> convert
