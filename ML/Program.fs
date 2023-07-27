open Microsoft.ML
open Microsoft.ML.Data
open Microsoft.ML.Trainers
open Microsoft.Data.Analysis
open Microsoft.ML.AutoML

let PrintMetrics (metrics: MulticlassClassificationMetrics) =
    printfn "Micro Accuracy: %.2f" metrics.MicroAccuracy
    printfn "Macro Accuracy: %.2f" metrics.MacroAccuracy
    printfn "Log Loss: %.2f" metrics.LogLoss
    printfn "Log Loss Reduction: %.2f\n" metrics.LogLossReduction

    printfn "%s" (metrics.ConfusionMatrix.GetFormattedConfusionTable())

let (+/) dir1 dir2 = System.IO.Path.Combine(dir1, dir2)

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

let downcastPipeline (x: IEstimator<_>) =
    match x with
    | :? IEstimator<ITransformer> as y -> y
    | _ -> failwith "downcastPipeline: expecting a IEstimator<ITransformer>"

// STEP 1: Common data loading configuration
let trainTestData (ctx: MLContext) =
    let trainDataPath = "data" +/ "traindata.txt"

    let textLoader =
        ctx.Data.CreateTextLoader(
            separatorChar = '\t',
            hasHeader = true,
            columns =
                [| TextLoader.Column("Label", DataKind.String, 0)
                   TextLoader.Column("Filename", DataKind.String, 1) |]
        )

    let trainingDataView' = textLoader.Load([| trainDataPath |])
    // let testDataView = textLoader.Load([| testDataPath |])

    // let trainTestSplit =
    ctx.Data.TrainTestSplit(trainingDataView', testFraction = 0.2, seed = 1)

// STEP 2: Common data process configuration with pipeline data transformations
let buildDataProcessingPipeline (ctx: MLContext) =
    EstimatorChain()
        .Append(ctx.Transforms.Conversion.MapValueToKey("Label", "Label"))
        .Append(ctx.Transforms.Text.FeaturizeText("Features", "Filename"))
        .AppendCacheCheckpoint(ctx)
    |> downcastPipeline

// STEP 3: Create the selected training algorithm/trainer
let sdcaNonCalibratedTrainer (ctx: MLContext) =
    ctx.MulticlassClassification.Trainers.SdcaNonCalibrated(labelColumnName = "Label", featureColumnName = "Features")
    |> downcastPipeline

let sdcaMaximumEntropyTrainer (ctx: MLContext) =
    ctx.MulticlassClassification.Trainers.SdcaMaximumEntropy(labelColumnName = "Label", featureColumnName = "Features")
    |> downcastPipeline

let lbfgsMaximumEntropyTrainer (ctx: MLContext) =
    ctx.MulticlassClassification.Trainers.LbfgsMaximumEntropy(labelColumnName = "Label", featureColumnName = "Features")
    |> downcastPipeline

let naiveBayesTrainer (ctx: MLContext) =
    ctx.MulticlassClassification.Trainers.NaiveBayes(labelColumnName = "Label", featureColumnName = "Features")
    |> downcastPipeline

//Set the trainer/algorithm
let buildTrainingPipeline (ctx: MLContext) (trainer: IEstimator<ITransformer>) =
    buildDataProcessingPipeline(ctx)
        .Append(trainer)
        .Append(ctx.Transforms.Conversion.MapKeyToValue("PredictedLabel"))

// STEP 4: Cross-Validate with single dataset (since we don't have two datasets, one for training and for evaluate)
// in order to evaluate and get the model's accuracy metrics
let crossValidationResults
    (ctx: MLContext)
    (trainTestSplit: DataOperationsCatalog.TrainTestData)
    estimator
    algorithmName
    =
    printfn "=============== Cross-validating to get model's accuracy metrics ==============="
    let results =
        ctx.MulticlassClassification.CrossValidate(
            data = trainTestSplit.TestSet,
            estimator = downcastPipeline estimator,
            numberOfFolds = 6,
            labelColumnName = "Label"
        )
        |> Seq.toArray

    Lib.printMulticlassClassificationFoldsAverageMetrics algorithmName results
    results

// STEP 5: Train the model fitting to the DataSet
let trainModel
    (trainTestSplit: DataOperationsCatalog.TrainTestData)
    (estimator: EstimatorChain<Transforms.KeyToValueMappingTransformer>)
    =
    printfn "=============== Training the model ==============="
    estimator.Fit(trainTestSplit.TrainSet)

// STEP 6: Try/test prediction with the "just-trained model" (Before saving the model)
let testPrediction (ctx: MLContext) (trainedModel: ITransformer) (trainTestSplit: DataOperationsCatalog.TrainTestData) =
    printfn "\n=============== Testing the model ==============="
    let predictionIDV = trainedModel.Transform trainTestSplit.TestSet
    // let columnsToSelect = [| "Filename"; "PredictedLabel" |]
    // predictionIDV.ToDataFrame(columnsToSelect).Rows
    // |> Seq.iter (fun row -> printfn "%-10s %s" (row.[1].ToString()) (row.[0].ToString()))

    // Evaluate the model
    let metrics = ctx.MulticlassClassification.Evaluate(predictionIDV)

    // Display the evaluation results
    printfn "Evaluation Results:"
    printfn "-------------------"
    printfn "Log-loss: %f" metrics.LogLoss
    printfn "Per-class log-loss:"
    for i = 0 to metrics.PerClassLogLoss.Count - 1 do
        printfn "Class %d: %f" i metrics.PerClassLogLoss.[i]

    PrintMetrics(metrics)

// STEP 7: Save the model
let saveModel (ctx: MLContext) trainedModel schema (filename: string) =
    ctx.Model.Save(trainedModel, schema, filename)

// Create MLContext to be shared across the model creation workflow objects
// Set a random seed for repeatable/deterministic results across multiple trainings.
let mlContext = MLContext(seed = 0)

let data = trainTestData mlContext

let listOfTrainers =
    [ sdcaNonCalibratedTrainer
      sdcaMaximumEntropyTrainer
      lbfgsMaximumEntropyTrainer
      naiveBayesTrainer ]

let executeTrainers1 context functionList =
    List.fold
        (fun acc f ->
            let trainer = f context
            let trainingPipeline = buildTrainingPipeline mlContext trainer
            let results =
                crossValidationResults mlContext data trainingPipeline (trainer.ToString())
            (trainer, trainingPipeline, results) :: acc)
        []
        functionList
    |> List.rev

executeTrainers1 mlContext listOfTrainers |> List.iter (printfn "%A")

let trainer = sdcaNonCalibratedTrainer mlContext
let trainingPipeline = buildTrainingPipeline mlContext trainer
// crossValidationResults mlContext data trainingPipeline (trainer.ToString())

let trainedModel = trainModel data trainingPipeline

testPrediction mlContext trainedModel data

printfn "_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_"
(*
// Set up the AutoML experiment to find the best algorithm
let experiment =
    mlContext
        .Auto()
        .CreateMulticlassClassificationExperiment(maxExperimentTimeInSeconds = (uint32 60))

// Run the experiment to find the best algorithm
let crossValidationResults' = experiment.Execute(data.TrainSet, "Label", "Features")

// Get the best model from the experiment
let bestModel = crossValidationResults'.BestRun.Model
let trainerName = crossValidationResults'.BestRun.TrainerName

printfn "%s\n" trainerName
testPrediction mlContext bestModel data

saveModel mlContext bestModel data.TrainSet.Schema ("data" +/ "B3NewsModel.zip")
*)

saveModel mlContext trainedModel data.TrainSet.Schema ("data" +/ "B3NewsModel.zip")
