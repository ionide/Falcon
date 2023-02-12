open FSharp.Compiler.Interactive.Shell
open System
open System.IO
open Suave
open Suave.Operators
open Suave.Filters
open FSharp.Compiler.Symbols
open System.Threading
open Falcon
open Suave.Sockets
open Suave.Sockets.Control
open Suave.WebSocket

type ValueResponse =
    { Name: string
      Type: string
      Value: string }

let getValuesAsString (fsiSession: FsiEvaluationSession) =
    fsiSession.GetBoundValues()
    |> List.map (fun v ->
        { Name = v.Name
          Type = v.Value.ReflectionType.ToString()
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

let mutable webSocket: WebSocket option = None

let ws (ws: WebSocket) (context: HttpContext) =
    socket {
        // if `loop` is set to false, the server will stop receiving messages
        let mutable loop = true
        webSocket <- Some ws

        while loop do
            // the server will wait for a message to be received without blocking the thread
            let! msg = ws.read ()

            match msg with
            | (Close, _, _) ->
                let emptyResponse = [||] |> ByteSegment
                do! ws.send Close emptyResponse true
                loop <- false

            | _ -> ()
    }


let router fsiSession =
    choose [ path "/ws" >=> handShake ws
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
             >=> Writers.setHeader "Content-Type" "application/json; charset=UTF-8" ]

[<EntryPoint>]
let main argv =
    let fsiConfig = FsiEvaluationSession.GetDefaultConfiguration()

    let stdin = Console.In
    let stdout = Console.Out
    let stderr = Console.Error

    ReadLine.HistoryEnabled <- true

    let mutable fsi: FsiEvaluationSession option = None

    let autocompleteHandler =
        { new IAutoCompleteHandler with
            member _.GetSuggestions(text: string, index: int) : string [] =
                let text = text.TrimEnd()
                let parts = text.Split(' ')
                let lastPart = parts.[parts.Length - 1]
                let lastPart = lastPart.TrimStart()

                match fsi with
                | None -> [||]
                | Some fsi -> fsi.GetCompletions(lastPart) |> Seq.toArray


            member _.Separators
                with get (): char [] = [| ' '; '.' |]
                and set (v: char []): unit = () }

    ReadLine.AutoCompletionHandler <- autocompleteHandler


    let customStdin =
        let mutable justPeaked = false

        { new TextReader() with

            member _.ReadLine() =
                let line =
                    //hack to workaround the initial read line
                    if justPeaked then
                        justPeaked <- false
                        let x = stdin.ReadLine()
                        ReadLine.AddHistory x
                        x
                    else
                        ReadLine.Read()

                line

            member _.Peek() =
                let line = stdin.Peek()
                justPeaked <- true
                line }

    let argv = Array.append argv [| "C:\\fsi.exe" |]

    let fsiSession =
        FsiEvaluationSession.Create(fsiConfig, argv, customStdin, stdout, stderr)

    fsi <- Some fsiSession

    fsiSession.ValueBound.Add (fun (value, b, c) ->
        webSocket
        |> Option.iter (fun ws ->
            let symbols =
                [| { Name = c
                     Type = b.FullName
                     Value = value.ToString() }
                   yield! getValuesAsString fsiSession |]

            ws.send Text (symbols |> Json.toJson |> ByteSegment) true
            |> Async.Ignore
            |> Async.RunSynchronously))

    let cts = new CancellationTokenSource()

    let emptyLogger =
        { new Logging.Logger with
            member this.log (arg1: Logging.LogLevel) (arg2: Logging.LogLevel -> Logging.Message) : unit = ()

            member this.logWithAck (arg1: Logging.LogLevel) (arg2: Logging.LogLevel -> Logging.Message) : Async<unit> =
                Async.result ()

            member this.name: string [] = [| "" |] }

    let conf =
        { defaultConfig with
            cancellationToken = cts.Token
            logger = emptyLogger }

    let _listening, server = startWebServerAsync conf (router fsiSession)

    Async.Start(server, cts.Token)

    fsiSession.Run()
    cts.Cancel()

    0
