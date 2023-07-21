open Microsoft.ML
open Microsoft.ML.Data
open Microsoft.ML.Trainers
open Microsoft.Data.Analysis

let PrintMetrics (metrics : MulticlassClassificationMetrics) =
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
let testDataPath = "data" +/ "testdata.txt"

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

let trainingDataView = textLoader.Load([| trainDataPath  |])
let testDataView = textLoader.Load([| testDataPath |])

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
type MyTrainerStrategy =
    | SdcaMultiClassTrainer
    | OVAAveragedPerceptronTrainer

let selectedStrategy = MyTrainerStrategy.SdcaMultiClassTrainer

let trainer =
    match selectedStrategy with
    | MyTrainerStrategy.SdcaMultiClassTrainer ->
        mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy("Label", "Features")
        |> downcastPipeline

    | MyTrainerStrategy.OVAAveragedPerceptronTrainer ->
        let averagedPerceptronBinaryTrainer =
            mlContext.BinaryClassification.Trainers.AveragedPerceptron("Label", "Features", numberOfIterations = 10)

        let downcastTrainer (x: ITrainerEstimator<_, _>) =
            match x with
            | :? ITrainerEstimator<_, _> as y -> y
            | _ -> failwith "downcastPipeline: expecting a ITrainerEstimator"

        let averagedPerceptronBinaryTrainer' =
            downcastTrainer averagedPerceptronBinaryTrainer

        // Compose an OVA (One-Versus-All) trainer with the BinaryTrainer.
        // In this strategy, a binary classification algorithm is used to train one classifier for each class, "
        // which distinguishes that class from all other classes. Prediction is then performed by running these binary classifiers, "
        // and choosing the prediction with the highest confidence score.
        mlContext.MulticlassClassification.Trainers.OneVersusAll(averagedPerceptronBinaryTrainer')
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
        data = trainingDataView,
        estimator = downcastPipeline modelBuilder,
        numberOfFolds = 6,
        labelColumnName = "Label"
    )

crossValidationResults
|> Seq.toArray
|> Lib.printMulticlassClassificationFoldsAverageMetrics (trainer.ToString())

// STEP 5: Train the model fitting to the DataSet
printfn "=============== Training the model ==============="
let trainedModel = modelBuilder.Fit(trainingDataView)


printfn "\n=============== Testing the model ==============="
let predictionIDV = trainedModel.Transform testDataView
let columnsToSelect = [| "Filename"; "PredictedLabel" |]
predictionIDV
    .ToDataFrame(columnsToSelect)
    .Rows 
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

PrintMetrics(metrics);


// (OPTIONAL) Try/test a single prediction with the "just-trained model" (Before saving the model)
let issue =
    { Label = "unknown"
      Filename = "PETRORIO_PRIO_NM_Fato_Relevante_17_07_23.pdf" }

let predEngine =
    mlContext.Model.CreatePredictionEngine<TextData, Prediction>(trainedModel)

let prediction = predEngine.Predict(issue)

printfn "\n=============== Single Prediction just-trained-model - Result: %s ===============" prediction.Label

// STEP 5: Save the model
mlContext.Model.Save(trainedModel, trainingDataView.Schema, "data" +/ "B3NewsModel.zip")