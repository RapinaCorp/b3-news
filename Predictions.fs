module Predicitions

open Microsoft.ML
open Microsoft.ML.Data
open System.IO

type TextData = {
    [<LoadColumn(0)>] Label: string
    [<LoadColumn(1)>] Filename: string
}

[<CLIMutable>]
type Prediction = {
    [<ColumnName("PredictedLabel")>] Label: string
}

let setPrediction (model: string) =
    let mlContext2 = MLContext();
    let loadedModel, _ = mlContext2.Model.Load model
    mlContext2.Model.CreatePredictionEngine<TextData, Prediction>(loadedModel)

let predictFolder (predictionEngine: PredictionEngine<TextData,Prediction>) filename =
    let data: TextData = { Label = ""; Filename = filename }
    predictionEngine.Predict(data).Label

