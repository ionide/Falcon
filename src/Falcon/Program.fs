open FSharp.Compiler.Interactive.Shell
open System.Diagnostics
open System
open System.IO
open Suave
open FSharp.Compiler.Symbols
open System.Threading

let getValuesAsString (fsiSession: FsiEvaluationSession) =
    fsiSession.GetBoundValues()
    |> List.map (fun v ->
        v.Name
        + ": "
        + v.Value.FSharpType.Format FSharpDisplayContext.Empty
        + " = "
        + v.Value.ReflectionValue.ToString())
    |> String.concat "\n"

[<EntryPoint>]
let main argv =
    let fsiConfig = FsiEvaluationSession.GetDefaultConfiguration()

    let stdin = Console.In
    let stdout = Console.Out
    let stderr = Console.Error

    let argv = Array.append argv [| "C:\\fsi.exe" |]

    let fsiSession = FsiEvaluationSession.Create(fsiConfig, argv, stdin, stdout, stderr)

    let cts = new CancellationTokenSource()
    let conf = { defaultConfig with cancellationToken = cts.Token }

    let _listening, server =
        startWebServerAsync conf (warbler (fun ctx -> (Successful.OK(getValuesAsString fsiSession))))

    Async.Start(server, cts.Token)

    printfn "Startin Falcon..."
    fsiSession.Run()
    printfn "Closing Falcon..."
    cts.Cancel()

    0
