namespace Newtonsoft.Json.FSharp.Idiomatic

open System
open FSharp.Reflection

open Newtonsoft.Json

/// F# options-converter
type OptionConverter() =
  inherit JsonConverter()
  let optionTy = typedefof<option<_>>

  override __.CanConvert t =
    t.IsGenericType
    && optionTy.Equals (t.GetGenericTypeDefinition())

  override __.WriteJson(writer, value, serializer) =
    let value =
      if isNull value then
        null
      else
        let _,fields = FSharpValue.GetUnionFields(value, value.GetType())
        fields.[0]
    serializer.Serialize(writer, value)

  override __.ReadJson(reader, t, _existingValue, serializer) =
    let innerType = t.GetGenericArguments().[0]

    let innerType =
      if innerType.IsValueType then
        typedefof<Nullable<_>>.MakeGenericType([| innerType |])
      else
        innerType

    let value = serializer.Deserialize(reader, innerType)
    let cases = FSharpType.GetUnionCases t

    if isNull value then
      FSharpValue.MakeUnion(cases.[0], [||])
    else
      FSharpValue.MakeUnion(cases.[1], [|value|])

module Reflection =
  open Newtonsoft.Json.Linq

  let allCasesEmpty (y: System.Type) = y |> FSharpType.GetUnionCases |> Array.forall (fun case -> case.GetFields() |> Array.isEmpty)
  let isList (y: System.Type) = y.IsGenericType && typedefof<List<_>> = y.GetGenericTypeDefinition ()
  let isOption (y: System.Type) = y.IsGenericType && typedefof<_ option> = y.GetGenericTypeDefinition ()

  let (|MapKey|_|) (key: 'a) map = map |> Map.tryFind key

  let getPropsAndReaders (reader: JsonReader) : Map<string, JsonReader> =
    match reader.TokenType with
    Map.empty

open Reflection

/// A converter that seamlessly converts enum-style discriminated unions, that is unions where every case has no data attached to it
type SingleCaseDuConverter () =
  inherit JsonConverter ()

  override __.CanConvert y =
    FSharpType.IsUnion y && allCasesEmpty y

  override __.WriteJson(writer, value, serializer) =
    // writing a single-case union is just the 'name' of the case
    let case, _ = FSharpValue.GetUnionFields(value, value.GetType())
    serializer.Serialize(writer, case.Name)

  /// reading a single-case union is just constructing a new instance of that particular case
  override __.ReadJson(reader, t, _existingValue, _serializer) =
    // printfn "type name is %s" t.FullName
    // printfn "token type is %s" (string reader.TokenType)
    let caseName = string reader.Value
    // printfn "Case name is %s" caseName
    let case = FSharpType.GetUnionCases t |> Array.tryFind (fun c -> c.Name.Equals(caseName, StringComparison.OrdinalIgnoreCase))
    match case with
    | Some case ->
      FSharpValue.MakeUnion(case, [||])
    | None ->
      failwithf "Unknown union case %s for Union Type %s" caseName t.FullName

/// a serializer that will handle DUs with fields, as long as the kind comes first.
type MultiCaseDuConverter (casePropertyName) =
  inherit JsonConverter ()

  new () = MultiCaseDuConverter("kind")

  override __.CanConvert y =
    FSharpType.IsUnion y
    && not (allCasesEmpty y)
    && not (isList y)
    && not (isOption y)

  override __.WriteJson(writer, value, serializer) =
    /// writes an output object whose properties are { "kind": CASE_NAME, "case_prop1": case_value_1 }, etc.
    let case, fields = FSharpValue.GetUnionFields(value, value.GetType())
    let propInfos = case.GetFields() |> Array.map (fun propInfo -> propInfo.Name);
    let zippedProperties = Array.zip fields propInfos

    writer.WriteStartObject ();
    // write case name
    writer.WritePropertyName casePropertyName
    writer.WriteValue case.Name

    // write properties
    for (field, name) in zippedProperties do
      writer.WritePropertyName name
      serializer.Serialize(writer, field)

    writer.WriteEndObject()

  override __.ReadJson(reader, t, _existingValue, serializer) =
    let cases = FSharpType.GetUnionCases t
    let rec loop (reader: JsonReader) caseAndFields propValues =
      // printfn "tokentype %s" (string reader.TokenType)
      // printfn "value %s" (string reader.Value)
      // should start at StartObject
      match reader.TokenType, caseAndFields with
      | JsonToken.StartObject, _
      | JsonToken.EndObject, None ->
//      | JsonToken.StartArray, _
//      | JsonToken.EndArray, _
//      | JsonToken.None, _ ->
        reader.Read () |> ignore
        loop reader caseAndFields propValues
      | JsonToken.PropertyName, None when string reader.Value = casePropertyName ->
        let caseName = reader.ReadAsString()
        match cases |> Seq.tryFind (fun c -> c.Name = caseName) with
        | Some case ->
          let fields = case.GetFields()
          reader.Read () |> ignore
          loop reader (Some (case, fields)) Map.empty
        | None ->
          failwithf "unknown case %s for type %s" caseName t.FullName
      | JsonToken.PropertyName, None ->
        // unordered fields :(
        failwithf "unordered field %s" (string reader.Value)
      | JsonToken.PropertyName, (Some (_, fields) as caseAndFields) ->
        let fieldName = string reader.Value
        match fields |> Array.tryFind (fun field -> field.Name = fieldName) with
        | Some field ->
          reader.Read () |> ignore
          let fieldValue = serializer.Deserialize(reader, field.PropertyType)
          reader.Read () |> ignore
          loop reader caseAndFields (propValues |> Map.add fieldName fieldValue)
        | None ->
          failwithf "unknown field %s" (string reader.Value)
      | JsonToken.EndObject, Some (case, fields) ->
        let fieldsInOrder = fields |> Array.map (fun f -> Map.find f.Name propValues)
        FSharpValue.MakeUnion(case, fieldsInOrder)
      | x ->
        failwithf "unhandled token type %s" (string x)
    loop reader None Map.empty

/// a serializer that looks at the propertyNames of fields as they come in and attempts to serialize the fields as soon as there is a distinguishing property name
type OutOfOrderMultiCaseDuConverter () =
  inherit JsonConverter ()

  let advance (reader: JsonReader) = reader.Read() |> ignore

  override __.CanConvert y =
    FSharpType.IsUnion y
    && not (Reflection.allCasesEmpty y)
    && not (Reflection.isList y)
    && not (Reflection.isOption y)

  override __.WriteJson (writer, value, serializer) =
    // writes an output object whose properties are the fields of the DU.
    let case, fields = FSharpValue.GetUnionFields(value, value.GetType())
    let propInfos = case.GetFields() |> Array.map (fun propInfo -> propInfo.Name);
    let zippedProperties = Array.zip fields propInfos

    writer.WriteStartObject ();
    for (field, name) in zippedProperties do
      writer.WritePropertyName name
      serializer.Serialize(writer, field)
    writer.WriteEndObject()

  override __.ReadJson (reader, t, _existingValue, serializer) =
    /// while reading, we'll look at a property name and see if it's distinct. if it is, then great!
    let cases = FSharpType.GetUnionCases t
    let casesByName = cases |> Array.map (fun case -> case.Name, case) |> Map.ofArray
    let fieldsByCaseName = cases |> Array.map (fun case -> case.Name, case.GetFields()) |> Map.ofArray
    let fieldNamesByCase = fieldsByCaseName |> Map.map (fun _case fields  -> fields |> Seq.map (fun field -> field.Name) |> Set.ofSeq)
    let findCaseByProperties allProps =
      let propSet = allProps |> Set.ofSeq
      match fieldNamesByCase |> Map.filter (fun _key fields -> fields = propSet) |> Map.toList with
      | [] -> failwithf "no case of type `%s` has properties named %A" t.AssemblyQualifiedName propSet
      | [(case, _fields)] -> casesByName |> Map.find case
      | cases -> failwithf "mutiple cases (%A) of type `%s` have properties named %A" (cases |> List.map fst) t.AssemblyQualifiedName propSet

    let propsAndReaders = getPropsAndReaders reader
    let case = findCaseByProperties (propsAndReaders |> Map.toSeq |> Seq.map fst)
    let casePropsInOrder = fieldsByCaseName |> Map.find case.Name |> Array.map (fun prop -> propsAndReaders |> Map.find prop.Name |> fun r -> serializer.Deserialize(r, prop.PropertyType))
    FSharpValue.MakeUnion(case, casePropsInOrder)

