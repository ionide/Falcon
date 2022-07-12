namespace Falcon

module List =

    ///Returns the greatest of all elements in the list that is less than the threshold
    let maxUnderThreshold nmax =
        List.maxBy (fun n -> if n > nmax then 0 else n)



[<AutoOpen>]
module TypedAstExtensionHelpers =
    open FSharp.Compiler.Symbols
    open System

    type FSharpGenericParameterMemberConstraint with
        member x.IsProperty =
            (x.MemberIsStatic
             && x.MemberArgumentTypes.Count = 0)
            || (not x.MemberIsStatic
                && x.MemberArgumentTypes.Count = 1)


    type FSharpEntity with
        member x.UnAnnotate() =
            let rec realEntity (s: FSharpEntity) =
                if s.IsFSharpAbbreviation then
                    realEntity s.AbbreviatedType.TypeDefinition
                else
                    s

            realEntity x

    type FSharpMemberOrFunctionOrValue with
        member x.EnclosingEntitySafe =
            try
                x.DeclaringEntity
            with
            | :? InvalidOperationException -> None

        member x.IsOperatorOrActivePattern =
            x.CompiledName.StartsWith "op_"
            || let name = x.DisplayName in

               if name.StartsWith "( "
                  && name.EndsWith " )"
                  && name.Length > 4 then
                   name.Substring(2, name.Length - 4)
                   |> String.forall (fun c -> c <> ' ')
               else
                   false

[<AutoOpen>]
module PrintParameter =
    let print sb = Printf.bprintf sb "%s"


module SignatureFormatter =
    open FSharp.Compiler.Symbols
    open FSharp.Compiler.Syntax
    open FSharp.Compiler.Tokenization
    open System
    open System.Text

    let nl = Environment.NewLine
    let maxPadding = 20

    /// Concat two strings with a space between if both a and b are not IsNullOrWhiteSpace
    let internal (++) (a: string) (b: string) =
        match String.IsNullOrEmpty a, String.IsNullOrEmpty b with
        | true, true -> ""
        | false, true -> a
        | true, false -> b
        | false, false -> a + " " + b

    let entityIsArray (entity: FSharpEntity) =
        if entity.IsArrayType then
            true
        else if entity.IsFSharpAbbreviation then
            entity.UnAnnotate().IsArrayType
        else
            false

    let private measureTypeNames =
        Set.ofList [ "Microsoft.FSharp.Core.CompilerServices.MeasureOne"
                     "Microsoft.FSharp.Core.CompilerServices.MeasureInverse`1"
                     "Microsoft.FSharp.Core.CompilerServices.MeasureProduct`2" ]

    let private isMeasureType (t: FSharpEntity) =
        Set.contains t.FullName measureTypeNames

    let rec formatFSharpType (context: FSharpDisplayContext) (typ: FSharpType) : string =
        let context = context.WithPrefixGenericParameters()

        try
            if typ.IsTupleType || typ.IsStructTupleType then
                let refTupleStr =
                    typ.GenericArguments
                    |> Seq.map (fun arg ->
                        if arg.IsTupleType && not arg.IsStructTupleType then
                            formatFSharpType context arg |> sprintf "(%s)"
                        else
                            formatFSharpType context arg)
                    |> String.concat " * "

                if typ.IsStructTupleType then
                    sprintf "struct (%s)" refTupleStr
                else
                    refTupleStr
            elif typ.IsGenericParameter then
                (if typ.GenericParameter.IsSolveAtCompileTime then
                     "^"
                 else
                     "'")
                + typ.GenericParameter.Name
            elif typ.HasTypeDefinition
                 && typ.GenericArguments.Count > 0 then
                let typeDef = typ.TypeDefinition

                let genericArgs =
                    typ.GenericArguments
                    |> Seq.map (formatFSharpType context)
                    |> String.concat ","

                if entityIsArray typeDef then
                    if typ.GenericArguments.Count = 1
                       && typ.GenericArguments.[0].IsTupleType then
                        sprintf "(%s) array" genericArgs
                    else
                        sprintf "%s array" genericArgs
                elif isMeasureType typeDef then
                    typ.Format context
                else
                    sprintf "%s<%s>" (FSharpKeywords.AddBackticksToIdentifierIfNeeded typeDef.DisplayName) genericArgs
            else if typ.HasTypeDefinition then
                FSharpKeywords.AddBackticksToIdentifierIfNeeded typ.TypeDefinition.DisplayName
            else
                typ.Format context
        with
        | _ -> typ.Format context

    let formatGenericParameter includeMemberConstraintTypes displayContext (param: FSharpGenericParameter) =

        let asGenericParamName (param: FSharpGenericParameter) =
            (if param.IsSolveAtCompileTime then
                 "^"
             else
                 "'")
            + param.Name

        let sb = StringBuilder()

        let getConstraint (constrainedBy: FSharpGenericParameterConstraint) =
            let memberConstraint (c: FSharpGenericParameterMemberConstraint) =
                let formattedMemberName, isProperty =
                    match c.IsProperty, PrettyNaming.TryChopPropertyName c.MemberName with
                    | true, Some (chopped) when chopped <> c.MemberName -> chopped, true
                    | _, _ ->
                        if PrettyNaming.IsMangledOpName c.MemberName then
                            $"( {PrettyNaming.DecompileOpName c.MemberName} )", false
                        else
                            c.MemberName, false

                seq {
                    if c.MemberIsStatic then yield "static "
                    yield "member "
                    yield formattedMemberName

                    if includeMemberConstraintTypes then
                        yield " : "

                        if isProperty then
                            yield (formatFSharpType displayContext c.MemberReturnType)
                        else
                            if c.MemberArgumentTypes.Count <= 1 then
                                yield "unit"
                            else
                                yield asGenericParamName param

                            yield " -> "

                            yield
                                ((formatFSharpType displayContext c.MemberReturnType)
                                    .TrimStart())
                }
                |> String.concat ""

            let typeConstraint (tc: FSharpType) =
                sprintf ":> %s" (formatFSharpType displayContext tc)

            let enumConstraint (ec: FSharpType) =
                sprintf "enum<%s>" (formatFSharpType displayContext ec)

            let delegateConstraint (tc: FSharpGenericParameterDelegateConstraint) =
                sprintf
                    "delegate<%s, %s>"
                    (formatFSharpType displayContext tc.DelegateTupledArgumentType)
                    (formatFSharpType displayContext tc.DelegateReturnType)

            let symbols =
                match constrainedBy with
                | _ when constrainedBy.IsCoercesToConstraint -> Some(typeConstraint constrainedBy.CoercesToTarget)
                | _ when constrainedBy.IsMemberConstraint -> Some(memberConstraint constrainedBy.MemberConstraintData)
                | _ when constrainedBy.IsSupportsNullConstraint -> Some("null")
                | _ when constrainedBy.IsRequiresDefaultConstructorConstraint -> Some("default constructor")
                | _ when constrainedBy.IsReferenceTypeConstraint -> Some("reference")
                | _ when constrainedBy.IsEnumConstraint -> Some(enumConstraint constrainedBy.EnumConstraintTarget)
                | _ when constrainedBy.IsComparisonConstraint -> Some("comparison")
                | _ when constrainedBy.IsEqualityConstraint -> Some("equality")
                | _ when constrainedBy.IsDelegateConstraint ->
                    Some(delegateConstraint constrainedBy.DelegateConstraintData)
                | _ when constrainedBy.IsUnmanagedConstraint -> Some("unmanaged")
                | _ when constrainedBy.IsNonNullableValueTypeConstraint -> Some("struct")
                | _ -> None

            symbols

        if param.Constraints.Count > 0 then
            param.Constraints
            |> Seq.choose getConstraint
            |> Seq.distinct
            |> Seq.iteri (fun i symbol ->
                if i > 0 then print sb " and "
                print sb symbol)

        sb.ToString()

    let getUnioncaseSignature (displayContext: FSharpDisplayContext) (unionCase: FSharpUnionCase) =
        if unionCase.Fields.Count > 0 then
            let typeList =
                unionCase.Fields
                |> Seq.map (fun unionField ->
                    if unionField.Name.StartsWith "Item" then //TODO: Some better way of dettecting default names for the union cases' fields
                        formatFSharpType displayContext unionField.FieldType
                    else
                        unionField.Name
                        ++ ":"
                        ++ (formatFSharpType displayContext unionField.FieldType))
                |> String.concat " * "

            unionCase.DisplayName + " of " + typeList
        else
            unionCase.DisplayName

    let getFuncSignatureWithIdent displayContext (func: FSharpMemberOrFunctionOrValue) (ident: int) =
        let maybeGetter = func.LogicalName.StartsWith "get_"
        let indent = String.replicate ident " "

        let functionName =
            let name =
                if func.IsConstructor then
                    match func.EnclosingEntitySafe with
                    | Some ent -> ent.DisplayName
                    | _ -> func.DisplayName
                    |> FSharpKeywords.AddBackticksToIdentifierIfNeeded
                elif func.IsOperatorOrActivePattern then
                    $"( {func.DisplayNameCore} )"
                elif func.DisplayName.StartsWith "( " then
                    FSharpKeywords.AddBackticksToIdentifierIfNeeded func.LogicalName
                else
                    FSharpKeywords.AddBackticksToIdentifierIfNeeded func.DisplayName

            name

        let modifiers =
            let accessibility =
                match func.Accessibility with
                | a when a.IsInternal -> "internal"
                | a when a.IsPrivate -> "private"
                | _ -> ""

            let modifier =
                //F# types are prefixed with new, should non F# types be too for consistancy?
                if func.IsConstructor then
                    match func.EnclosingEntitySafe with
                    | Some ent ->
                        if ent.IsFSharp then
                            "new" ++ accessibility
                        else
                            accessibility
                    | _ -> accessibility
                elif func.IsProperty then
                    if func.IsInstanceMember then
                        if func.IsDispatchSlot then
                            "abstract property" ++ accessibility
                        else
                            "property" ++ accessibility
                    else
                        "static property" ++ accessibility
                elif func.IsMember then
                    if func.IsInstanceMember then
                        if func.IsDispatchSlot then
                            "abstract member" ++ accessibility
                        else
                            "member" ++ accessibility
                    else
                        "static member" ++ accessibility
                else if func.InlineAnnotation = FSharpInlineAnnotation.AlwaysInline then
                    "val" ++ accessibility ++ "inline"
                elif func.IsInstanceMember then
                    "val" ++ accessibility
                else
                    "val" ++ accessibility //does this need to be static prefixed?

            modifier

        let argInfos =
            func.CurriedParameterGroups
            |> Seq.map Seq.toList
            |> Seq.toList

        let retType =
            //This try block will be removed when FCS updates
            try
                formatFSharpType displayContext func.ReturnParameter.Type
            with
            | _ex -> "Unknown"

        let retTypeConstraint =
            if func.ReturnParameter.Type.IsGenericParameter then
                let formattedParam =
                    formatGenericParameter false displayContext func.ReturnParameter.Type.GenericParameter

                if String.IsNullOrWhiteSpace formattedParam then
                    formattedParam
                else
                    "(requires " + formattedParam + " )"
            else
                ""

        let safeParameterName (p: FSharpParameter) =
            match Option.defaultValue p.DisplayNameCore p.Name with
            | "" -> ""
            | name -> FSharpKeywords.AddBackticksToIdentifierIfNeeded name

        let padLength =
            let allLengths =
                argInfos
                |> List.concat
                |> List.map (fun p -> (safeParameterName p).Length)

            match allLengths with
            | [] -> 0
            | l -> l |> List.maxUnderThreshold maxPadding

        let formatName indent padding (parameter: FSharpParameter) =
            let name = safeParameterName parameter
            indent + name.PadRight padding + ":"

        let isDelegate =
            match func.EnclosingEntitySafe with
            | Some ent -> ent.IsDelegate
            | _ -> false

        let formatParameter (p: FSharpParameter) =
            try
                let formatted = formatFSharpType displayContext p.Type

                if p.Type.IsFunctionType then
                    $"({formatted})"
                else
                    formatted
            with
            | :? InvalidOperationException -> p.DisplayName

        match argInfos with
        | [] ->
            //When does this occur, val type within  module?
            if isDelegate then
                retType
            else
                modifiers ++ functionName + ":" ++ retType

        | _ when func.IsProperty -> modifiers ++ functionName + ":" ++ retType
        | [ [] ] ->
            if isDelegate then
                retType
            //A ctor with () parameters seems to be a list with an empty list.
            // Also abstract members and abstract member overrides with one () parameter seem to be a list with an empty list.
            elif func.IsConstructor
                 || (func.IsMember && (not func.IsPropertyGetterMethod)) then
                modifiers + ": unit -> " ++ retType
            else
                modifiers ++ functionName + ":" ++ retType //Value members seems to be a list with an empty list
        | [ [ p ] ] when maybeGetter && formatParameter p = "unit" -> //Member or property with only getter
            modifiers ++ functionName + ":" ++ retType
        | many ->

            let allParamsLengths =
                many
                |> List.map (
                    List.map (fun p -> (formatParameter p).Length)
                    >> List.sum
                )

            let maxLength =
                (allParamsLengths
                 |> List.maxUnderThreshold maxPadding)
                + 1

            let formatParameterPadded length p =
                let namePart = formatName indent padLength p
                let paramType = formatParameter p
                let paramFormat = namePart ++ paramType

                if p.Type.IsGenericParameter then
                    let padding =
                        String.replicate
                            (if length >= maxLength then
                                 1
                             else
                                 maxLength - length)
                            " "

                    let paramConstraint =
                        let formattedParam =
                            formatGenericParameter false displayContext p.Type.GenericParameter

                        if String.IsNullOrWhiteSpace formattedParam then
                            formattedParam
                        else
                            "(requires " + formattedParam + " )"

                    if paramConstraint = retTypeConstraint then
                        paramFormat
                    else
                        paramFormat + padding + paramConstraint
                else
                    paramFormat

            let allParams =
                List.zip many allParamsLengths
                |> List.map (fun (paramTypes, length) ->
                    paramTypes
                    |> List.map (formatParameterPadded length)
                    |> String.concat $" *{nl}")
                |> String.concat $" ->{nl}"

            let typeArguments =
                let padding = String.replicate (max (padLength - 1) 0) " "

                $"{allParams}{nl}{indent}{padding}->"
                ++ retType
                ++ retTypeConstraint

            if isDelegate then
                typeArguments
            else
                modifiers ++ $"{functionName}:{nl}{typeArguments}"

    let getFuncSignatureForTypeSignature
        displayContext
        (func: FSharpMemberOrFunctionOrValue)
        (overloads: int)
        (getter: bool)
        (setter: bool)
        =
        let functionName =
            let name =
                if func.IsConstructor then
                    "new"
                elif func.IsOperatorOrActivePattern then
                    func.DisplayName
                elif func.DisplayName.StartsWith "( " then
                    FSharpKeywords.AddBackticksToIdentifierIfNeeded func.LogicalName
                elif func.LogicalName.StartsWith "get_"
                     || func.LogicalName.StartsWith "set_" then
                    PrettyNaming.TryChopPropertyName func.DisplayName
                    |> Option.defaultValue func.DisplayName
                else
                    func.DisplayName

            name

        let modifiers =
            let accessibility =
                match func.Accessibility with
                | a when a.IsInternal -> "internal"
                | a when a.IsPrivate -> "private"
                | _ -> ""

            let modifier =
                if func.IsConstructor then
                    accessibility
                elif func.IsProperty then
                    if func.IsInstanceMember then
                        if func.IsDispatchSlot then
                            "abstract property" ++ accessibility
                        else
                            "property" ++ accessibility
                    else
                        "static property" ++ accessibility
                elif func.IsMember then
                    if func.IsInstanceMember then
                        if func.IsDispatchSlot then
                            "abstract member" ++ accessibility
                        else
                            "member" ++ accessibility
                    else
                        "static member" ++ accessibility
                else if func.InlineAnnotation = FSharpInlineAnnotation.AlwaysInline then
                    "val" ++ accessibility ++ "inline"
                elif func.IsInstanceMember then
                    "val" ++ accessibility
                else
                    "val" ++ accessibility //does this need to be static prefixed?

            modifier

        let argInfos =
            func.CurriedParameterGroups
            |> Seq.map Seq.toList
            |> Seq.toList

        let retType =
            //This try block will be removed when FCS updates
            try
                if func.IsConstructor then
                    match func.EnclosingEntitySafe with
                    | Some ent -> ent.DisplayName
                    | _ -> func.DisplayName
                    |> FSharpKeywords.AddBackticksToIdentifierIfNeeded
                else
                    formatFSharpType displayContext func.ReturnParameter.Type
            with
            | _ex ->
                try
                    if func.FullType.GenericArguments.Count > 0 then
                        let lastArg = func.FullType.GenericArguments |> Seq.last
                        formatFSharpType displayContext lastArg
                    else
                        "Unknown"
                with
                | _ -> "Unknown"

        let formatName (parameter: FSharpParameter) =
            parameter.Name
            |> Option.defaultValue parameter.DisplayName

        let isDelegate =
            match func.EnclosingEntitySafe with
            | Some ent -> ent.IsDelegate
            | _ -> false

        let res =
            match argInfos with
            | [] ->
                //When does this occur, val type within  module?
                if isDelegate then
                    retType
                else
                    modifiers ++ functionName + ": " ++ retType

            | _ when func.IsProperty -> modifiers ++ functionName + ": " ++ retType
            | [ [] ] ->
                if isDelegate then
                    retType
                elif func.IsConstructor then
                    modifiers + ": unit ->" ++ retType //A ctor with () parameters seems to be a list with an empty list
                else
                    modifiers ++ functionName + ": " ++ retType //Value members seems to be a list with an empty list
            | many ->
                let formatParameter (p: FSharpParameter) =
                    try
                        let formatted = formatFSharpType displayContext p.Type

                        if p.Type.IsFunctionType then
                            $"({formatted})"
                        else
                            formatted
                    with
                    | :? InvalidOperationException -> p.DisplayName

                let allParams =
                    many
                    |> List.map (fun (paramTypes) ->
                        paramTypes
                        |> List.map (fun p -> formatName p + ":" ++ (formatParameter p))
                        |> String.concat (" * "))
                    |> String.concat ("-> ")

                let typeArguments = allParams ++ "->" ++ retType

                if isDelegate then
                    typeArguments
                else
                    modifiers ++ functionName + ": " + typeArguments

        let res =
            if overloads = 1 then
                res
            else
                sprintf "%s (+ %i overloads)" res (overloads - 1)

        match getter, setter with
        | true, true -> res ++ "with get,set"
        | true, false -> res ++ "with get"
        | false, true -> res ++ "with set"
        | false, false -> res

    let getFieldSignature displayContext (field: FSharpField) =
        let retType = formatFSharpType displayContext field.FieldType

        match field.LiteralValue with
        | Some lv ->
            field.DisplayName + ":"
            ++ retType
            ++ "="
            ++ (string lv)
        | None ->
            let prefix =
                if field.IsMutable then
                    "val mutable"
                else
                    "val"

            prefix ++ field.DisplayName + ":" ++ retType

    let getEntitySignature displayContext (fse: FSharpEntity) =
        let modifier =
            match fse.Accessibility with
            | a when a.IsInternal -> "internal "
            | a when a.IsPrivate -> "private "
            | _ -> ""

        let typeName =
            match fse with
            | _ when fse.IsFSharpModule -> "module"
            | _ when fse.IsEnum -> "enum"
            | _ when fse.IsValueType -> "struct"
            | _ when fse.IsNamespace -> "namespace"
            | _ when fse.IsFSharpRecord -> "record"
            | _ when fse.IsFSharpUnion -> "union"
            | _ when fse.IsInterface -> "interface"
            | _ -> "type"

        let enumtip () =
            $" ={nl}  |"
            ++ (fse.FSharpFields
                |> Seq.filter (fun f -> not f.IsCompilerGenerated)
                |> Seq.map (fun field ->
                    match field.LiteralValue with
                    | Some lv -> field.Name + " = " + (string lv)
                    | None -> field.Name)
                |> String.concat $"{nl}  | ")

        let uniontip () =
            $" ={nl}  |"
            ++ (fse.UnionCases
                |> Seq.map (getUnioncaseSignature displayContext)
                |> String.concat $"{nl}  | ")

        let delegateTip () =
            let invoker =
                fse.MembersFunctionsAndValues
                |> Seq.find (fun f -> f.DisplayName = "Invoke")

            let invokerSig = getFuncSignatureWithIdent displayContext invoker 6
            $" ={nl}   delegate of{nl}{invokerSig}"

        let typeTip () =
            let constrc =
                fse.MembersFunctionsAndValues
                |> Seq.filter (fun n -> n.IsConstructor && n.Accessibility.IsPublic)
                |> fun v ->
                    match Seq.tryHead v with
                    | None -> ""
                    | Some f ->
                        let l = Seq.length v
                        getFuncSignatureForTypeSignature displayContext f l false false

            let fields =
                fse.FSharpFields
                |> Seq.filter (fun n -> n.Accessibility.IsPublic) //TODO: If defined in same project as current scope then show also internals
                |> Seq.sortBy (fun n -> n.DisplayName)
                |> Seq.map (getFieldSignature displayContext)

            let fields =
                if Seq.length fields > 11 then
                    seq {
                        yield! Seq.take 11 fields
                        yield "..."
                    }
                else
                    fields

            let funcs =
                fse.MembersFunctionsAndValues
                |> Seq.filter (fun n -> n.Accessibility.IsPublic && (not n.IsConstructor)) //TODO: If defined in same project as current scope then show also internals
                |> Seq.groupBy (fun n -> n.FullName)
                |> Seq.map (fun (_, v) ->
                    match v |> Seq.tryFind (fun f -> f.IsProperty) with
                    | Some prop ->
                        let getter =
                            v
                            |> Seq.exists (fun f -> f.IsPropertyGetterMethod)

                        let setter =
                            v
                            |> Seq.exists (fun f -> f.IsPropertySetterMethod)

                        getFuncSignatureForTypeSignature displayContext prop 1 getter setter //Ensure properties are displayed only once, properly report
                    | None ->
                        let f = Seq.head v
                        let l = Seq.length v
                        getFuncSignatureForTypeSignature displayContext f l false false)

            let funcs =
                if Seq.length funcs > 11 then
                    seq {
                        yield! Seq.take 11 funcs
                        yield "..."
                    }
                else
                    funcs


            let res =
                [ yield constrc
                  if not fse.IsFSharpModule then
                      yield! fields
                      if Seq.length fields > 0 then yield nl
                      yield! funcs ]
                |> Seq.distinct
                |> String.concat $"{nl}  "

            if String.IsNullOrWhiteSpace res then
                ""
            else
                $"{nl}  {res}"

        let typeDisplay =
            let name =
                let normalisedName = FSharpKeywords.AddBackticksToIdentifierIfNeeded fse.DisplayName

                if fse.GenericParameters.Count > 0 then
                    let paramsAndConstraints =
                        fse.GenericParameters
                        |> Seq.groupBy (fun p -> p.Name)
                        |> Seq.map (fun (name, constraints) ->
                            let renderedConstraints =
                                constraints
                                |> Seq.map (formatGenericParameter false displayContext)
                                |> String.concat " and"

                            if String.IsNullOrWhiteSpace renderedConstraints then
                                "'" + name
                            else
                                sprintf "'%s (requires %s)" name renderedConstraints)

                    normalisedName
                    + "<"
                    + (paramsAndConstraints |> String.concat ",")
                    + ">"
                else
                    normalisedName

            let basicName = modifier + typeName ++ name

            if fse.IsFSharpAbbreviation then
                let unannotatedType = fse.UnAnnotate()
                basicName ++ "=" ++ (unannotatedType.DisplayName)
            else
                basicName

        if fse.IsFSharpUnion then
            typeDisplay + uniontip ()
        elif fse.IsEnum then
            typeDisplay + enumtip ()
        elif fse.IsDelegate then
            typeDisplay + delegateTip ()
        else
            typeDisplay + typeTip ()
