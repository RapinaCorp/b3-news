module Log

open System

let setConsoleColor (color: ConsoleColor) =
    Console.ForegroundColor <- color

let resetConsoleColor () =
    Console.ResetColor()

let printMessageWithColor (color: ConsoleColor) (message: string) =
    setConsoleColor color
    printfn "%s" message
    resetConsoleColor ()

let emergency message =
    printMessageWithColor ConsoleColor.Red message

let alert message =
    printMessageWithColor ConsoleColor.DarkRed message

let critical message =
    printMessageWithColor ConsoleColor.DarkMagenta message

let error message =
    printMessageWithColor ConsoleColor.Red message

let warning message =
    printMessageWithColor ConsoleColor.DarkYellow message

let notice message =
    printMessageWithColor ConsoleColor.Cyan message

let info message =
    printMessageWithColor ConsoleColor.Blue message

let debug message =
    printMessageWithColor ConsoleColor.DarkGray message
