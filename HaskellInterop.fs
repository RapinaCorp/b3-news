module HaskellInterop

module List =   
    // The apply function for lists
    // [f;g] apply [x;y] becomes [f x; f y; g x; g y]
    let apply (fList: ('a->'b) list) (xList: 'a list)  =
        List.collect (fun f -> List.map f xList) fList

// let (<*>) = List.apply                                  

module Result =
    let transform (transform: 'a -> 'b) (inputResult: Result<'a list, string>) : Result<'b list, string> =
        match inputResult with
        | Ok inputList ->
            // Apply the transformation function to each element in the inputList
            let transformedList = List.map transform inputList
            Ok transformedList
        | Error errorMessage ->
            // Propagate the error message in the output result
            Error errorMessage

