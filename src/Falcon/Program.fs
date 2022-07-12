open FSharp.Compiler.Interactive.Shell
open System.Diagnostics
open System
open System.IO
open Suave
open Suave.Operators
open Suave.Filters
open FSharp.Compiler.Symbols
open System.Threading
open Falcon

type ValueResponse =
    { Name: string
      Type: string
      Value: string }

let getValuesAsString (fsiSession: FsiEvaluationSession) =
    fsiSession.GetBoundValues()
    |> List.map (fun v ->
        { Name = v.Name
          Type = v.Value.FSharpType.Format FSharpDisplayContext.Empty
          Value = v.Value.ReflectionValue.ToString() })
    |> List.toArray

type TypeResponse = { Name: string; Signature: string }

let getTypesAsString (fsiSession: FsiEvaluationSession) =
    fsiSession.CurrentPartialAssemblySignature.Entities
    |> Seq.collect (fun e -> e.NestedEntities)
    |> Seq.map (fun e ->
        { Name = e.DisplayName
          Signature = SignatureFormatter.getEntitySignature FSharpDisplayContext.Empty e })
    |> Seq.toArray

let router fsiSession =
    GET
    >=> choose [ path "/values"
                 >=> (warbler (fun ctx ->
                     (getValuesAsString fsiSession)
                     |> Json.toJson
                     |> Successful.ok))
                 path "/types"
                 >=> (warbler (fun ctx ->
                     (getTypesAsString fsiSession
                      |> Json.toJson
                      |> Successful.ok))) ]
    >=> Writers.setHeader "Content-Type" "application/json; charset=UTF-8"

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



    let _listening, server = startWebServerAsync conf (router fsiSession)

    Async.Start(server, cts.Token)

    printfn "Startin Falcon..."
    fsiSession.Run()
    printfn "Closing Falcon..."
    cts.Cancel()

    0
