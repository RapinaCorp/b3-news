open Microsoft.ML
open Microsoft.ML.Data
open Microsoft.ML.Trainers
open Microsoft.Data.Analysis

let PrintMetrics (metrics: MulticlassClassificationMetrics) =
    printfn "Micro Accuracy: %.2f" metrics.MicroAccuracy
    printfn "Macro Accuracy: %.2f" metrics.MacroAccuracy
    printfn "Log Loss: %.2f" metrics.LogLoss
    printfn "Log Loss Reduction: %.2f\n" metrics.LogLossReduction

    printfn "%s" (metrics.ConfusionMatrix.GetFormattedConfusionTable())

let (+/) dir1 dir2 = System.IO.Path.Combine(dir1, dir2)

// Create MLContext to be shared across the model creation workflow objects
// Set a random seed for repeatable/deterministic results across multiple trainings.
let mlContext = MLContext(seed = 0)

// STEP 1: Common data loading configuration
let trainDataPath = "data" +/ "traindata.txt"

type TextData =
    { [<LoadColumn(0)>]
      Label: string
      [<LoadColumn(1)>]
      Filename: string }

// Define the prediction class
[<CLIMutable>]
type Prediction =
    { [<ColumnName("PredictedLabel")>]
      Label: string }

let textLoader =
    mlContext.Data.CreateTextLoader(
        separatorChar = '\t',
        hasHeader = true,
        columns =
            [| TextLoader.Column("Label", DataKind.String, 0)
               TextLoader.Column("Filename", DataKind.String, 1) |]
    )

let trainingDataView' = textLoader.Load([| trainDataPath |])
// let testDataView = textLoader.Load([| testDataPath |])

let trainTestSplit =
    mlContext.Data.TrainTestSplit(trainingDataView', testFraction = 0.2, seed = 1)

// STEP 2: Common data process configuration with pipeline data transformations
let downcastPipeline (x: IEstimator<_>) =
    match x with
    | :? IEstimator<ITransformer> as y -> y
    | _ -> failwith "downcastPipeline: expecting a IEstimator<ITransformer>"

let dataProcessPipeline =
    EstimatorChain()
        .Append(mlContext.Transforms.Conversion.MapValueToKey("Label", "Label"))
        .Append(mlContext.Transforms.Text.FeaturizeText("Features", "Filename"))
        .AppendCacheCheckpoint(mlContext)
    |> downcastPipeline

// STEP 3: Create the selected training algorithm/trainer
//mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy("Label", "Features")
let trainer =
    mlContext.MulticlassClassification.Trainers.SdcaNonCalibrated(labelColumnName = "Label", featureColumnName = "Features")
        |> downcastPipeline

//Set the trainer/algorithm
let modelBuilder =
    dataProcessPipeline
        .Append(trainer)
        .Append(mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"))

// STEP 4: Cross-Validate with single dataset (since we don't have two datasets, one for training and for evaluate)
// in order to evaluate and get the model's accuracy metrics
printfn "=============== Cross-validating to get model's accuracy metrics ==============="

let crossValidationResults =
    mlContext.MulticlassClassification.CrossValidate(
        data = trainTestSplit.TrainSet,
        estimator = downcastPipeline modelBuilder,
        numberOfFolds = 6,
        labelColumnName = "Label"
    )

crossValidationResults
|> Seq.toArray
|> Lib.printMulticlassClassificationFoldsAverageMetrics (trainer.ToString())

// STEP 5: Train the model fitting to the DataSet
printfn "=============== Training the model ==============="
let trainedModel = modelBuilder.Fit(trainTestSplit.TrainSet)

// STEP 6: Try/test prediction with the "just-trained model" (Before saving the model)

printfn "\n=============== Testing the model ==============="
let predictionIDV = trainedModel.Transform trainTestSplit.TestSet
let columnsToSelect = [| "Filename"; "PredictedLabel" |]
predictionIDV.ToDataFrame(columnsToSelect).Rows
|> Seq.iter (fun row -> printfn "%-10s %s" (row.[1].ToString()) (row.[0].ToString()))

// Evaluate the model
let metrics = mlContext.MulticlassClassification.Evaluate(predictionIDV)

// Display the evaluation results
printfn "Evaluation Results:"
printfn "-------------------"
printfn "Log-loss: %f" metrics.LogLoss
printfn "Per-class log-loss:"
for i = 0 to metrics.PerClassLogLoss.Count - 1 do
    printfn "Class %d: %f" i metrics.PerClassLogLoss.[i]

PrintMetrics(metrics)

// STEP 7: Save the model
mlContext.Model.Save(trainedModel, trainTestSplit.TrainSet.Schema, "data" +/ "B3NewsModel.zip")
