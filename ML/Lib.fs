module Lib

open Microsoft.ML
open Microsoft.ML.Data
open System

let private calculateStandardDeviation (values: float array) =
    let average = values |> Array.average
    let sumOfSquaresOfDifferences =
        values |> Array.map (fun v -> (v - average) * (v - average)) |> Array.sum
    let standardDeviation =
        Math.Sqrt(sumOfSquaresOfDifferences / float (values.Length - 1))
    standardDeviation

let calculateConfidenceInterval95 (values: float array) =
    let confidenceInterval95 =
        1.96 * calculateStandardDeviation (values)
        / Math.Sqrt(float (values.Length - 1))
    confidenceInterval95

let printMulticlassClassificationFoldsAverageMetrics
    algorithmName
    (crossValResults: TrainCatalogBase.CrossValidationResult<MulticlassClassificationMetrics>[])
    =

    let metricsInMultipleFolds = crossValResults |> Array.map (fun r -> r.Metrics)

    let microAccuracyValues =
        metricsInMultipleFolds |> Array.map (fun m -> m.MicroAccuracy)
    let microAccuracyAverage = microAccuracyValues |> Array.average
    let microAccuraciesStdDeviation = calculateStandardDeviation microAccuracyValues
    let microAccuraciesConfidenceInterval95 =
        calculateConfidenceInterval95 microAccuracyValues

    let macroAccuracyValues =
        metricsInMultipleFolds |> Array.map (fun m -> m.MicroAccuracy)
    let macroAccuracyAverage = macroAccuracyValues |> Array.average
    let macroAccuraciesStdDeviation = calculateStandardDeviation macroAccuracyValues
    let macroAccuraciesConfidenceInterval95 =
        calculateConfidenceInterval95 macroAccuracyValues

    let logLossValues = metricsInMultipleFolds |> Array.map (fun m -> m.LogLoss)
    let logLossAverage = logLossValues |> Array.average
    let logLossStdDeviation = calculateStandardDeviation logLossValues
    let logLossConfidenceInterval95 = calculateConfidenceInterval95 logLossValues

    let logLossReductionValues =
        metricsInMultipleFolds |> Array.map (fun m -> m.LogLossReduction)
    let logLossReductionAverage = logLossReductionValues |> Array.average
    let logLossReductionStdDeviation = calculateStandardDeviation logLossReductionValues
    let logLossReductionConfidenceInterval95 =
        calculateConfidenceInterval95 logLossReductionValues

    printfn
        "*************************************************************************************************************"
    printfn "*       Metrics for %s Multi-class Classification model      " algorithmName
    printfn
        "*------------------------------------------------------------------------------------------------------------"
    printfn
        "*       Average MicroAccuracy:    %.3f  - Standard deviation: (%.3f)  - Confidence Interval 95%%: (%.3f)"
        microAccuracyAverage
        microAccuraciesStdDeviation
        microAccuraciesConfidenceInterval95
    printfn
        "*       Average MacroAccuracy:    %.3f  - Standard deviation: (%.3f)  - Confidence Interval 95%%: (%.3f)"
        macroAccuracyAverage
        macroAccuraciesStdDeviation
        macroAccuraciesConfidenceInterval95
    printfn
        "*       Average LogLoss:          %.3f  - Standard deviation: (%.3f)  - Confidence Interval 95%%: (%.3f)"
        logLossAverage
        logLossStdDeviation
        logLossConfidenceInterval95
    printfn
        "*       Average LogLossReduction: %.3f  - Standard deviation: (%.3f)  - Confidence Interval 95%%: (%.3f)"
        logLossReductionAverage
        logLossReductionStdDeviation
        logLossReductionConfidenceInterval95
    printfn
        "*************************************************************************************************************"

let PrintMetrics (metrics: MulticlassClassificationMetrics) =
    printfn "Micro Accuracy (best: 1):              %.3f" metrics.MicroAccuracy
    printfn "Macro Accuracy (best: 1):              %.3f" metrics.MacroAccuracy
    printfn "Log Loss (best: 0):                    %.3f" metrics.LogLoss
    printfn "Log Loss Reduction (best: 1, mean: 0): %.3f\n" metrics.LogLossReduction

    printfn "%s" (metrics.ConfusionMatrix.GetFormattedConfusionTable())

let takeRand len count =
    let random = System.Random()
    [ for _ in 1 .. (max 100 (len * 2)) -> random.Next(len) ]
    |> List.distinct
    |> List.take count
