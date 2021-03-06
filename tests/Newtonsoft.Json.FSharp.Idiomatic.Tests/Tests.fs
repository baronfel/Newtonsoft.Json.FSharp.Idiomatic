module Tests

open Expecto
open Newtonsoft.Json.FSharp.Idiomatic
open Newtonsoft.Json

let singleCaseDuConverter = new SingleCaseDuConverter()
let multiCaseConverter = new MultiCaseDuConverter ()
let optionConverter = new OptionConverter ()
let outoforderDuConverter = new OutOfOrderMultiCaseDuConverter()

let converters: JsonConverter list = [
  singleCaseDuConverter
//  multiCaseConverter
  outoforderDuConverter
  optionConverter
]

let settings converters =
  let settings = JsonSerializerSettings()
  for converter in converters do
    settings.Converters.Add converter
  settings

let inline serializer converters =
  let settings = settings converters
  fun o -> JsonConvert.SerializeObject(o, settings)

let inline deserializer<'a> (converters: JsonConverter list) s=
  let settings = settings converters
  JsonConvert.DeserializeObject<'a>(s, settings)

type SingleCaseDU =
| A
| B

type DeserializationTargetSingle =
  { things: SingleCaseDU }

type MostlySingleCaseDU =
| A
| B of value: string

[<Tests>]
let singleCaseTests =
  testList "Single-Case DU Conversion" [
    let serializer, deserializer = serializer [singleCaseDuConverter], deserializer [singleCaseDuConverter]

    yield test "doesn't convert non-DU type" {
      Expect.isFalse (singleCaseDuConverter.CanConvert typeof<string>) "cannot convert non-du types"
    }

    yield test "can convert single-case DU type" {
      Expect.isTrue (singleCaseDuConverter.CanConvert typeof<SingleCaseDU>) "can convert all-single-case-DU"
    }

    yield test "cannot convert mixed-case DU" {
      Expect.isFalse (singleCaseDuConverter.CanConvert typeof<MostlySingleCaseDU>) "cannot convert DU with mixed cases"
    }

    yield test "can write case" {
      let result = serializer SingleCaseDU.A
      Expect.equal "\"A\"" result "should translate to string"
    }

    yield test "can read case" {
      let result = deserializer """{ "things" : "A"}"""
      Expect.equal result {things = SingleCaseDU.A } "should translate back again"
    }
  ]

type DeserializationTargetMulti =
  { things: MostlySingleCaseDU }


[<Tests>]
let multiCaseTests =
  testList "Multi-case DU Conversion" [
    let serializer, deserializer = serializer [multiCaseConverter], deserializer [multiCaseConverter]

    yield test "doesn't convert non-DU type" {
      Expect.isFalse (multiCaseConverter.CanConvert typeof<string>) "cannot convert non-du types"
    }

    yield  test "can't convert single-case DU type" {
      Expect.isFalse (multiCaseConverter.CanConvert typeof<SingleCaseDU>) "cannot convert all-single-case-DU"
    }

    yield test "can convert mixed-case DU" {
      Expect.isTrue (multiCaseConverter.CanConvert typeof<MostlySingleCaseDU>) "can convert DU with mixed cases"
    }

    yield test "can write case" {
      let result = serializer (MostlySingleCaseDU.B "foobar")
      Expect.equal result """{"kind":"B","value":"foobar"}""" "should serialize correctly"
    }
    yield test "can read case" {
      let result = deserializer """{"things":{"kind": "B", "value":"foo"}}"""
      Expect.equal result { things = MostlySingleCaseDU.B "foo" } "should translate back again"
    }
  ]

type Inner = {foo: string}
type Harness =
| TestCase of boolProp: bool * floatProp : float * intProp : int16 * strProp : string * arrProp : int list * nestedArrs : int list list * nestedObj : Inner * nestedObjArray : Inner list * optionalThing : int option

type Dupes =
| First of samename: bool
| Second of samename: bool

[<Tests>]
let outoforderMulticaseTests =
  testList "Out Of Order Multi-case DU conversion" [
    let serializer, deserializer = serializer [outoforderDuConverter], deserializer [outoforderDuConverter]
    let sample = TestCase (true, 1.234, 8s, "hiya", [1;2;3], [[1];[2];[3]], {foo = "hi"}, [{foo = "hi1"}; {foo = "hi2"}], None)
    let outStrNice =
      """ {
            "boolProp" : true,
            "floatProp": 1.234,
            "intProp"  : 8,
            "strProp"  : "hiya",
            "arrProp"  : [1,2,3],
            "nestedArrs":[
              [1],
              [2],
              [3]
            ],
            "nestedObj": {
              "foo": "hi"
            },
            "nestedObjArray": [
              { "foo": "hi1" },
              { "foo": "hi2" }
            ],
            "optionalThing":null
          }"""
    let outStr = System.Text.RegularExpressions.Regex(@"\s+").Replace(outStrNice, "")

    yield test "doesn't convert non-DU type" {
      Expect.isFalse (outoforderDuConverter.CanConvert typeof<string>) "cannot convert non-du types"
    }

    yield test "can't convert single-case DU type" {
      Expect.isFalse (outoforderDuConverter.CanConvert typeof<SingleCaseDU>) "cannot convert all-single-case-DU"
    }

    yield test "can convert mixed-case DU" {
      Expect.isTrue (outoforderDuConverter.CanConvert typeof<MostlySingleCaseDU>) "can convert DU with mixed cases"
    }

    yield test "can write case" {
      let result = serializer sample
      Expect.equal result outStr "should serialize correctly"
    }

    yield test "can read case" {
      let result = deserializer outStr
      Expect.equal result sample "should translate back again"
    }

    yield test "can read out of order case" {
      let outStrJumbled =
        """ {
              "nestedObj": {
                "foo": "hi"
              },
              "floatProp": 1.234,
              "boolProp" : true,
              "strProp"  : "hiya",
              "intProp"  : 8,
              "nestedArrs":[
                [1],
                [2],
                [3]
              ],
              "arrProp"  : [1,2,3],
              "nestedObjArray": [
                { "foo": "hi1" },
                { "foo": "hi2" }
              ]
            }"""
      let result = deserializer outStr
      Expect.equal result sample "should handle propnames out of order"
    }

    yield test "should fail if property list is not exact match" {
      let ``inner json with extra prop`` = """{"foo" : "bar", "thing":false}"""
      Expect.throws (fun _ -> deserializer ``inner json with extra prop`` |> ignore) "should't be able to handle extra props"
    }
  ]
