module MultiLabel

open System
open Microsoft.ML
open Microsoft.ML.Data
open Microsoft.ML.Transforms.Text

// Define your data class
type Report =
    { [<LoadColumn(0)>]
      Company: string
      [<LoadColumn(1)>]
      ReportType: string
      [<LoadColumn(2)>]
      Date: string
      [<LoadColumn(3)>]
      Filename: string }

// Define your prediction class (if you're using different classes for prediction)
[<CLIMutable>]
type Prediction =
    { [<ColumnName("PredictedLabel")>]
      PredictedLabel: string }

let (+/) dir1 dir2 = System.IO.Path.Combine(dir1, dir2)

let downcastPipeline (x: IEstimator<_>) =
    match x with
    | :? IEstimator<ITransformer> as y -> y
    | _ -> failwith "downcastPipeline: expecting a IEstimator<ITransformer>"

// Pipeline
let processData (ctx: MLContext) =
    let options =
        TextFeaturizingEstimator.Options(
            CaseMode = TextNormalizingEstimator.CaseMode.None,
            StopWordsRemoverOptions = CustomStopWordsRemovingEstimator.Options(StopWords = [| "_" |]),
            KeepNumbers = false,
            WordFeatureExtractor = WordBagEstimator.Options(NgramLength = 2)
        )

    EstimatorChain()
        .Append(ctx.Transforms.Conversion.MapValueToKey("Label", "Company"))
        .Append(ctx.Transforms.Text.FeaturizeText("Feature", options, "Filename"))
        .AppendCacheCheckpoint(ctx)

let BuildAndTrainModel (ctx: MLContext) dataView (pipeline: EstimatorChain<ITransformer>) =
    let trainingPipeline =
        pipeline
            .Append(ctx.MulticlassClassification.Trainers.SdcaNonCalibrated("Label", "Feature"))
            .Append(ctx.Transforms.Conversion.MapKeyToValue("PredictedLabel"))

    let trainedModel = trainingPipeline.Fit(dataView)

    let predEngine = ctx.Model.CreatePredictionEngine<Report, Prediction>(trainedModel)

    let report =
        { Company = "ABCD"
          ReportType = "X"
          Date = "2023-02-02"
          Filename = "2023_06_09_18_18_55_FII_CSHG_LOG_HGLG_Fato_Relevante_09_06_2023.pdf" }
    let prediction = predEngine.Predict(report)
    printfn
        "=============== Single Prediction just-trained-model - Result: %s (%b) ==============="
        prediction.PredictedLabel
        (prediction.PredictedLabel = "HGLG")

    (trainingPipeline, trainedModel)

let run () =
    let ctx = MLContext(seed = 4)

    let trainDataPath = "data" +/ "multi-label-data.csv"
    let trainingDataView =
        ctx.Data.LoadFromTextFile<Report>(trainDataPath, separatorChar = ';', hasHeader = true, trimWhitespace = true)
    let data = ctx.Data.TrainTestSplit(trainingDataView, testFraction = 0.3, seed = 1)

    let pipeline = processData ctx
    let (trainingPipeline, trainedModel) = BuildAndTrainModel ctx data.TrainSet pipeline

    let results =
        ctx.MulticlassClassification.CrossValidate(
            data = data.TestSet,
            estimator = downcastPipeline trainingPipeline,
            numberOfFolds = 5,
            labelColumnName = "Label"
            // samplingKeyColumnName = "Company"
        )
        |> Seq.toArray
    Lib.printMulticlassClassificationFoldsAverageMetrics "SdcaNonCalibrated" results

    let testMetrics =
        ctx.MulticlassClassification.Evaluate(trainedModel.Transform(data.TestSet))
    Lib.PrintMetrics testMetrics

    printfn "\n=============== Testing the model ==============="
    let predictionIDV = trainedModel.Transform data.TestSet
    let columnsToSelect = [| "Filename"; "PredictedLabel" |]
    let rows = predictionIDV.ToDataFrame(columnsToSelect).Rows
    let idx = Lib.takeRand (rows |> Seq.length) 25
    idx
    |> Seq.iter (fun i -> 
        printfn "%-10s %s" (rows.[i].[1].ToString()) (rows.[i].[0].ToString())
        )


/////////////////////////////////////////////////////
(*
let downcastPipeline (x: IEstimator<_>) =
    match x with
    | :? IEstimator<ITransformer> as y -> y
    | _ -> failwith "downcastPipeline: expecting a IEstimator<ITransformer>"

// STEP 1: Common data loading configuration
let trainTestData (ctx: MLContext) =
    let trainDataPath = "data" +/ "multi-label-data.csv"

    let textLoader =
        ctx.Data.CreateTextLoader(
            separatorChar = ';',
            hasHeader = true,
            columns =
                [| TextLoader.Column("Company", DataKind.String, 0)
                   TextLoader.Column("ReportType", DataKind.String, 1)
                   TextLoader.Column("Filename", DataKind.String, 2) |]
        )

    let trainingDataView' = textLoader.Load([| trainDataPath |])
    // let testDataView = textLoader.Load([| testDataPath |])

    // let trainTestSplit =
    ctx.Data.TrainTestSplit(trainingDataView', testFraction = 0.2, seed = 2)

// STEP 2: Common data process configuration with pipeline data transformations
let buildDataProcessingPipeline (ctx: MLContext) =
    EstimatorChain()
        .Append(ctx.Transforms.Conversion.MapValueToKey("CompanyLabel", "Company"))
        // .Append(ctx.Transforms.Conversion.MapValueToKey("ReportTypeLabel", "ReportType"))
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

// PrintMetrics(metrics)

// STEP 7: Save the model
let saveModel (ctx: MLContext) trainedModel schema (filename: string) =
    ctx.Model.Save(trainedModel, schema, filename)

// Create MLContext to be shared across the model creation workflow objects
// Set a random seed for repeatable/deterministic results across multiple trainings.
let mlContext = MLContext(seed = 2)

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

executeTrainers1 mlContext listOfTrainers |> ignore

let trainer = sdcaNonCalibratedTrainer mlContext
let trainingPipeline = buildTrainingPipeline mlContext trainer
// crossValidationResults mlContext data trainingPipeline (trainer.ToString())

let trainedModel = trainModel data trainingPipeline

let run2 () =
    printfn "_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_"
    testPrediction mlContext trainedModel data
    saveModel mlContext trainedModel data.TrainSet.Schema ("data" +/ "B3MultiLabel.zip")
    printfn "_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_+_"
*)
