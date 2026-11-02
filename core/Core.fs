module MSNRBFEditor.Core

open System
open System.IO
open System.Text
open System.Collections.Generic
open System.Globalization

// ---------------------------------------------------------------------------
// MS-NRBF (the format used by .NET BinaryFormatter) primitive type codes.
// ---------------------------------------------------------------------------

type PrimitiveType =
    | Boolean = 1
    | Byte = 2
    | Char = 3
    | Decimal = 5
    | Double = 6
    | Int16 = 7
    | Int32 = 8
    | Int64 = 9
    | SByte = 10
    | Single = 11
    | TimeSpan = 12
    | DateTime = 13
    | UInt16 = 14
    | UInt32 = 15
    | UInt64 = 16
    | Null = 17
    | String = 18

/// The type of each member of a serialized class.
type BinaryType =
    | PrimitiveTypeInfo of PrimitiveType
    | StringType
    | ObjectType
    | SystemClassType of string
    | ClassType of string * int          // class name, library id
    | ObjectArrayType
    | StringArrayType
    | PrimitiveArrayType of PrimitiveType

type PrimitiveValue =
    | PBool of bool
    | PByte of byte
    | PChar of char
    | PDecimal of decimal
    | PDouble of double
    | PInt16 of int16
    | PInt32 of int
    | PInt64 of int64
    | PSByte of sbyte
    | PSingle of single
    | PTimeSpan of int64
    | PDateTime of int64
    | PUInt16 of uint16
    | PUInt32 of uint32
    | PUInt64 of uint64
    | PNull
    | PString of string

type ObjectKind =
    | KObject
    | KArray
    | KString

/// One object from the serialization stream.  The fields are mutable so that
/// an object can be registered by its id before its members are read (which is
/// how .NET BinaryFormatter supports cycles and shared references).
type SerializedValue =
    | NullVal
    | PrimVal of PrimitiveValue
    | RefVal of int
    | ObjVal of SerializedObject

and SerializedObject =
    { Id: int
      ClassName: string
      Kind: ObjectKind
      mutable Members: (string * SerializedValue) list
      mutable MemberTypes: BinaryType[]
      mutable LibraryId: int option
      mutable Items: SerializedValue list
      mutable Lengths: int[]
      mutable LowerBounds: int[] option
      mutable ArrayElementType: BinaryType option
      mutable ArrayType: byte option
      mutable Text: string }

type ClassMetadata =
    { Name: string
      MemberNames: string[]
      Types: BinaryType[]
      LibraryId: int option }

type HeaderInfo =
    { RootId: int
      HeaderId: int
      MajorVersion: int
      MinorVersion: int }

type ParsedRecord =
    | PValue of SerializedValue
    | PNulls of int
    | PHeader of HeaderInfo
    | PLibrary of int * string
    | PMessageEnd

let private makeObject id className kind =
    { Id = id
      ClassName = className
      Kind = kind
      Members = []
      MemberTypes = [||]
      LibraryId = None
      Items = []
      Lengths = [||]
      LowerBounds = None
      ArrayElementType = None
      ArrayType = None
      Text = "" }

let private toPrimitiveType (code: byte) : PrimitiveType =
    match code with
    | 1uy -> PrimitiveType.Boolean
    | 2uy -> PrimitiveType.Byte
    | 3uy -> PrimitiveType.Char
    | 5uy -> PrimitiveType.Decimal
    | 6uy -> PrimitiveType.Double
    | 7uy -> PrimitiveType.Int16
    | 8uy -> PrimitiveType.Int32
    | 9uy -> PrimitiveType.Int64
    | 10uy -> PrimitiveType.SByte
    | 11uy -> PrimitiveType.Single
    | 12uy -> PrimitiveType.TimeSpan
    | 13uy -> PrimitiveType.DateTime
    | 14uy -> PrimitiveType.UInt16
    | 15uy -> PrimitiveType.UInt32
    | 16uy -> PrimitiveType.UInt64
    | 17uy -> PrimitiveType.Null
    | 18uy -> PrimitiveType.String
    | _ -> failwithf "Unknown MS-NRBF primitive type code 0x%02x" code

// ---------------------------------------------------------------------------
// Reader
// ---------------------------------------------------------------------------

type NrbfReader(stream: Stream) =
    let reader = new BinaryReader(stream, Encoding.UTF8, true)
    let objects = Dictionary<int, SerializedObject>()
    let metadata = Dictionary<int, ClassMetadata>()
    let libraries = Dictionary<int, string>()
    let mutable header = None

    member _.Objects = objects
    member _.Metadata = metadata
    member _.Libraries = libraries
    member _.Header = header

    member private _.ReadInt32() = reader.ReadInt32()
    member private _.ReadInt64() = reader.ReadInt64()
    member private _.ReadByte() = reader.ReadByte()

    /// BinaryFormatter uses the 7-bit encoded length format from BinaryWriter.
    member private this.Read7BitEncodedInt() =
        let mutable result = 0
        let mutable shift = 0
        let mutable more = true
        while more do
            let b = reader.ReadByte()
            result <- result ||| ((int b &&& 0x7F) <<< shift)
            shift <- shift + 7
            more <- (b &&& 0x80uy) <> 0uy
        result

    member private this.ReadString() =
        let length = this.Read7BitEncodedInt()
        if length = 0 then
            ""
        else
            let bytes = reader.ReadBytes(length)
            if bytes.Length <> length then
                raise (EndOfStreamException("Unexpected end of stream while reading a string"))
            Encoding.UTF8.GetString(bytes)

    member private this.ReadPrimitiveValue(primitive: PrimitiveType) : PrimitiveValue =
        match primitive with
        | PrimitiveType.Boolean -> PBool (reader.ReadBoolean())
        | PrimitiveType.Byte -> PByte (reader.ReadByte())
        | PrimitiveType.Char -> PChar (reader.ReadChar())
        | PrimitiveType.Decimal -> PDecimal (reader.ReadDecimal())
        | PrimitiveType.Double -> PDouble (reader.ReadDouble())
        | PrimitiveType.Int16 -> PInt16 (reader.ReadInt16())
        | PrimitiveType.Int32 -> PInt32 (reader.ReadInt32())
        | PrimitiveType.Int64 -> PInt64 (reader.ReadInt64())
        | PrimitiveType.SByte -> PSByte (reader.ReadSByte())
        | PrimitiveType.Single -> PSingle (reader.ReadSingle())
        | PrimitiveType.TimeSpan -> PTimeSpan (reader.ReadInt64())
        | PrimitiveType.DateTime -> PDateTime (reader.ReadInt64())
        | PrimitiveType.UInt16 -> PUInt16 (reader.ReadUInt16())
        | PrimitiveType.UInt32 -> PUInt32 (reader.ReadUInt32())
        | PrimitiveType.UInt64 -> PUInt64 (reader.ReadUInt64())
        | PrimitiveType.Null -> PNull
        | PrimitiveType.String -> PString (this.ReadString())
        | other -> failwithf "Unsupported primitive type value %A" other

    member private this.ReadBinaryTypeInfo(code: byte) : BinaryType =
        match code with
        | 0uy -> PrimitiveTypeInfo(toPrimitiveType (this.ReadByte()))
        | 1uy -> StringType
        | 2uy -> ObjectType
        | 3uy -> SystemClassType(this.ReadString())
        | 4uy ->
            let className = this.ReadString()
            let libraryId = this.ReadInt32()
            ClassType(className, libraryId)
        | 5uy -> ObjectArrayType
        | 6uy -> StringArrayType
        | 7uy -> PrimitiveArrayType(toPrimitiveType (this.ReadByte()))
        | _ -> failwithf "Unknown MS-NRBF BinaryTypeEnum 0x%02x" code

    member private this.ReadBinaryType() : BinaryType =
        this.ReadBinaryTypeInfo(this.ReadByte())

    member private this.ReadClassInfo() =
        let objectId = this.ReadInt32()
        let name = this.ReadString()
        let memberCount = this.ReadInt32()
        if memberCount < 0 then
            failwithf "Negative member count %d for class %s" memberCount name
        let memberNames = Array.init memberCount (fun _ -> this.ReadString())
        objectId, name, memberNames

    member private this.ReadMemberTypeInfo(count: int) : BinaryType[] =
        if count < 0 then
            failwithf "Negative member type count %d" count
        let codes = Array.init count (fun _ -> this.ReadByte())
        codes |> Array.map this.ReadBinaryTypeInfo

    member private this.RegisterObject(obj: SerializedObject) =
        objects.[obj.Id] <- obj
        obj

    member private this.NewArray
        (
            objectId: int,
            lengths: int[],
            lowerBounds: int[] option,
            elementType: BinaryType,
            arrayType: byte
        ) =
        let obj = makeObject objectId "System.Array" KArray
        obj.Lengths <- lengths
        obj.LowerBounds <- lowerBounds
        obj.ArrayElementType <- Some elementType
        obj.ArrayType <- Some arrayType
        this.RegisterObject(obj)

    member private this.RegisterClassObject(objectId: int, meta: ClassMetadata) =
        let obj = this.RegisterObject(makeObject objectId meta.Name KObject)
        obj.MemberTypes <- meta.Types
        obj.LibraryId <- meta.LibraryId
        obj

    member private this.ReadMemberValues(memberNames: string[], types: BinaryType[]) =
        let values =
            types
            |> Array.map (fun binaryType ->
                match binaryType with
                | PrimitiveTypeInfo primitive -> PrimVal (this.ReadPrimitiveValue primitive)
                | _ -> this.ReadRecordValue())

        List.ofArray (Array.zip memberNames values)

    member private this.ReadRecordValue() : SerializedValue =
        let mutable result = None
        while result.IsNone do
            match this.ReadRecord() with
            | PValue value -> result <- Some value
            | PNulls _ -> result <- Some NullVal
            | PLibrary _ -> () // Library records can be interleaved with values.
            | PHeader _ -> failwith "Unexpected SerializationHeaderRecord while reading a value"
            | PMessageEnd -> failwith "Unexpected MessageEnd while reading a value"
        result.Value

    /// Reads array elements, expanding the ObjectNullMultiple records that
    /// BinaryFormatter uses to compact runs of null elements.
    member private this.ReadArrayItems(total: int) : SerializedValue list =
        if total < 0 then
            failwithf "Negative array length %d" total

        let items = ResizeArray<SerializedValue>(total)

        while items.Count < total do
            match this.ReadRecord() with
            | PValue value -> items.Add value
            | PNulls n ->
                let remaining = total - items.Count
                if n < 0 || n > remaining then
                    failwithf "Invalid null-run length %d (only %d array elements remain)" n remaining
                for _ in 1 .. n do
                    items.Add NullVal
            | PLibrary _ -> () // Library records can be interleaved with elements.
            | PHeader _ -> failwith "Unexpected SerializationHeaderRecord while reading an array"
            | PMessageEnd -> failwith "Unexpected MessageEnd while reading an array"

        List.ofSeq items

    member private this.ReadRecord() : ParsedRecord =
        let recordType = this.ReadByte()

        match recordType with
        | 0x00uy -> // SerializationHeaderRecord
            let rootId = this.ReadInt32()
            let headerId = this.ReadInt32()
            let major = this.ReadInt32()
            let minor = this.ReadInt32()
            let info =
                { RootId = rootId
                  HeaderId = headerId
                  MajorVersion = major
                  MinorVersion = minor }
            header <- Some info
            PHeader info

        | 0x01uy -> // ClassWithId
            let objectId = this.ReadInt32()
            let metadataId = this.ReadInt32()
            let meta =
                match metadata.TryGetValue(metadataId) with
                | true, value -> value
                | _ -> failwithf "ClassWithId refers to unknown metadata id %d" metadataId

            let obj = this.RegisterClassObject(objectId, meta)
            obj.Members <- this.ReadMemberValues(meta.MemberNames, meta.Types)
            PValue(ObjVal obj)

        | 0x02uy -> // SystemClassWithMembers
            let objectId, name, memberNames = this.ReadClassInfo()
            let types = Array.create memberNames.Length ObjectType
            let meta = { Name = name; MemberNames = memberNames; Types = types; LibraryId = None }
            metadata.[objectId] <- meta
            let obj = this.RegisterClassObject(objectId, meta)
            obj.Members <- this.ReadMemberValues(memberNames, types)
            PValue(ObjVal obj)

        | 0x03uy -> // ClassWithMembers
            let objectId, name, memberNames = this.ReadClassInfo()
            let libraryId = this.ReadInt32()
            let types = Array.create memberNames.Length ObjectType
            let meta =
                { Name = name
                  MemberNames = memberNames
                  Types = types
                  LibraryId = Some libraryId }
            metadata.[objectId] <- meta
            let obj = this.RegisterClassObject(objectId, meta)
            obj.Members <- this.ReadMemberValues(memberNames, types)
            PValue(ObjVal obj)

        | 0x04uy -> // SystemClassWithMembersAndTypes
            let objectId, name, memberNames = this.ReadClassInfo()
            let types = this.ReadMemberTypeInfo(memberNames.Length)
            let meta =
                { Name = name
                  MemberNames = memberNames
                  Types = types
                  LibraryId = None }
            metadata.[objectId] <- meta
            let obj = this.RegisterClassObject(objectId, meta)
            obj.Members <- this.ReadMemberValues(memberNames, types)
            PValue(ObjVal obj)

        | 0x05uy -> // ClassWithMembersAndTypes
            let objectId, name, memberNames = this.ReadClassInfo()
            let types = this.ReadMemberTypeInfo(memberNames.Length)
            let libraryId = this.ReadInt32()
            let meta =
                { Name = name
                  MemberNames = memberNames
                  Types = types
                  LibraryId = Some libraryId }
            metadata.[objectId] <- meta
            let obj = this.RegisterClassObject(objectId, meta)
            obj.Members <- this.ReadMemberValues(memberNames, types)
            PValue(ObjVal obj)

        | 0x06uy -> // BinaryObjectString
            let objectId = this.ReadInt32()
            let text = this.ReadString()
            let obj = this.RegisterObject(makeObject objectId "System.String" KString)
            obj.Text <- text
            PValue(ObjVal obj)

        | 0x07uy -> // BinaryArray
            let objectId = this.ReadInt32()
            let arrayType = this.ReadByte()
            let rank = this.ReadInt32()
            if rank < 0 then
                failwithf "Negative array rank %d" rank
            let lengths = Array.init rank (fun _ -> this.ReadInt32())
            let lowerBounds =
                if arrayType = 3uy || arrayType = 4uy || arrayType = 5uy then
                    Some(Array.init rank (fun _ -> this.ReadInt32()))
                else
                    None
            let elementType = this.ReadBinaryType()
            let total =
                lengths
                |> Array.fold (fun acc length -> acc * max 0 length) 1

            let obj = this.NewArray(objectId, lengths, lowerBounds, elementType, arrayType)
            obj.Items <-
                match elementType with
                | PrimitiveTypeInfo primitive ->
                    [ for _ in 1 .. total -> PrimVal(this.ReadPrimitiveValue primitive) ]
                | PrimitiveArrayType primitive ->
                    [ for _ in 1 .. total -> PrimVal(this.ReadPrimitiveValue primitive) ]
                | _ -> this.ReadArrayItems(total)
            PValue(ObjVal obj)

        | 0x08uy -> // MemberPrimitiveTyped
            let primitive = toPrimitiveType (this.ReadByte())
            PValue(PrimVal(this.ReadPrimitiveValue primitive))

        | 0x09uy -> // MemberReference
            PValue(RefVal(this.ReadInt32()))

        | 0x0Auy -> // ObjectNull
            PValue NullVal

        | 0x0Buy -> // MessageEnd
            PMessageEnd

        | 0x0Cuy -> // BinaryLibrary
            let libraryId = this.ReadInt32()
            let name = this.ReadString()
            libraries.[libraryId] <- name
            PLibrary(libraryId, name)

        | 0x0Duy -> // ObjectNullMultiple256
            PNulls(int (this.ReadByte()))

        | 0x0Euy -> // ObjectNullMultiple
            let count = this.ReadInt32()
            PNulls count

        | 0x0Fuy -> // ArraySinglePrimitive
            let objectId = this.ReadInt32()
            let length = this.ReadInt32()
            let primitive = toPrimitiveType (this.ReadByte())
            let obj =
                this.NewArray(objectId, [| length |], None, PrimitiveTypeInfo primitive, 0uy)
            obj.Items <- [ for _ in 1 .. length -> PrimVal(this.ReadPrimitiveValue primitive) ]
            PValue(ObjVal obj)

        | 0x10uy -> // ArraySingleObject
            let objectId = this.ReadInt32()
            let length = this.ReadInt32()
            let obj = this.NewArray(objectId, [| length |], None, ObjectType, 0uy)
            obj.Items <- this.ReadArrayItems(length)
            PValue(ObjVal obj)

        | 0x11uy -> // ArraySingleString
            let objectId = this.ReadInt32()
            let length = this.ReadInt32()
            let obj = this.NewArray(objectId, [| length |], None, StringType, 0uy)
            obj.Items <- this.ReadArrayItems(length)
            PValue(ObjVal obj)

        | other ->
            failwithf "Unknown MS-NRBF record type 0x%02x at offset %d" other (stream.Position - 1L)

    member this.Parse() =
        let mutable doneReading = false
        while not doneReading do
            match this.ReadRecord() with
            | PMessageEnd -> doneReading <- true
            | _ -> ()

// ---------------------------------------------------------------------------
// JSON model and writer
// ---------------------------------------------------------------------------

type JsonValue =
    | JNull
    | JBool of bool
    | JNumber of string
    | JString of string
    | JArray of JsonValue list
    | JObject of (string * JsonValue) list

module JsonWriter =

    let private escapeString (value: string) =
        let sb = StringBuilder(value.Length + 2)
        sb.Append('"') |> ignore
        for c in value do
            match c with
            | '"' -> sb.Append("\\\"") |> ignore
            | '\\' -> sb.Append("\\\\") |> ignore
            | '\b' -> sb.Append("\\b") |> ignore
            | '\f' -> sb.Append("\\f") |> ignore
            | '\n' -> sb.Append("\\n") |> ignore
            | '\r' -> sb.Append("\\r") |> ignore
            | '\t' -> sb.Append("\\t") |> ignore
            | c when c < ' ' ->
                sb.Append("\\u") |> ignore
                sb.Append((int c).ToString("x4", CultureInfo.InvariantCulture)) |> ignore
            | c -> sb.Append(c) |> ignore
        sb.Append('"') |> ignore
        sb.ToString()

    let rec private writeValue (sb: StringBuilder) (indent: bool) (level: int) (value: JsonValue) =
        let writeIndent level =
            if indent then
                sb.Append('\n') |> ignore
                sb.Append(String(' ', level * 2)) |> ignore

        match value with
        | JNull -> sb.Append("null") |> ignore
        | JBool b -> sb.Append(if b then "true" else "false") |> ignore
        | JNumber n -> sb.Append(n) |> ignore
        | JString s -> sb.Append(escapeString s) |> ignore

        | JArray [] -> sb.Append("[]") |> ignore
        | JArray items ->
            sb.Append('[') |> ignore
            items
            |> List.iteri (fun index item ->
                if index > 0 then
                    sb.Append(',') |> ignore
                writeIndent (level + 1)
                writeValue sb indent (level + 1) item)
            writeIndent level
            sb.Append(']') |> ignore

        | JObject [] -> sb.Append("{}") |> ignore
        | JObject properties ->
            sb.Append('{') |> ignore
            properties
            |> List.iteri (fun index (key, propertyValue) ->
                if index > 0 then
                    sb.Append(',') |> ignore
                writeIndent (level + 1)
                sb.Append(escapeString key) |> ignore
                sb.Append(if indent then ": " else ":") |> ignore
                writeValue sb indent (level + 1) propertyValue)
            writeIndent level
            sb.Append('}') |> ignore

    let serialize (indent: bool) (value: JsonValue) =
        let sb = StringBuilder()
        writeValue sb indent 0 value
        if indent then
            sb.Append('\n') |> ignore
        sb.ToString()

let private primitiveToJson (value: PrimitiveValue) : JsonValue =
    let invariant = CultureInfo.InvariantCulture
    match value with
    | PBool b -> JBool b
    | PByte b -> JNumber(b.ToString(invariant))
    | PChar c -> JString(string c)
    | PDecimal d -> JNumber(d.ToString(invariant))
    | PDouble d ->
        if Double.IsNaN(d) || Double.IsInfinity(d) then
            JString(d.ToString(invariant))
        else
            JNumber(d.ToString("R", invariant))
    | PInt16 n -> JNumber(n.ToString(invariant))
    | PInt32 n -> JNumber(n.ToString(invariant))
    | PInt64 n -> JNumber(n.ToString(invariant))
    | PSByte n -> JNumber(n.ToString(invariant))
    | PSingle n ->
        if Single.IsNaN(n) || Single.IsInfinity(n) then
            JString(n.ToString(invariant))
        else
            JNumber(n.ToString("R", invariant))
    | PTimeSpan ticks -> JString(TimeSpan.FromTicks(ticks).ToString("c", invariant))
    | PDateTime raw ->
        try
            JString(DateTime.FromBinary(raw).ToString("o", invariant))
        with _ ->
            JNumber(raw.ToString(invariant))
    | PUInt16 n -> JNumber(n.ToString(invariant))
    | PUInt32 n -> JNumber(n.ToString(invariant))
    | PUInt64 n -> JNumber(n.ToString(invariant))
    | PNull -> JNull
    | PString s -> JString s

let rec private nestArray (lengths: int[]) (items: JsonValue list) : JsonValue =
    match lengths with
    | [||] -> JArray items
    | [| _ |] -> JArray items
    | _ ->
        let innerCount =
            lengths.[1..]
            |> Array.fold (fun acc length -> acc * max 0 length) 1
            |> max 1

        let chunks = items |> List.chunkBySize innerCount
        JArray(chunks |> List.map (nestArray lengths.[1..]))

// ---------------------------------------------------------------------------
// Conversion from the serialized object graph to JSON.
//
// The raw MS-NRBF graph contains implementation details such as Hashtable
// buckets, List<T> backing arrays and enum value__ fields.  These are turned
// into natural JSON objects, arrays and scalars.
// ---------------------------------------------------------------------------

type JsonConverter(objects: Dictionary<int, SerializedObject>) =
    let resolving = HashSet<int>()

    member private _.Resolve(value: SerializedValue) : SerializedValue =
        match value with
        | RefVal id ->
            match objects.TryGetValue(id) with
            | true, obj -> ObjVal obj
            | _ -> NullVal
        | other -> other

    member private this.TryGetArrayItems(value: SerializedValue) : SerializedValue list option =
        match this.Resolve(value) with
        | ObjVal obj when obj.Kind = KArray -> Some obj.Items
        | _ -> None

    member private _.JsonKey(value: JsonValue) : string =
        match value with
        | JString s -> s
        | JNumber n -> n
        | JBool b -> if b then "true" else "false"
        | JNull -> "null"
        | other -> JsonWriter.serialize false other

    member private this.ConvertHashtable(obj: SerializedObject) : JsonValue =
        let tryMember name =
            obj.Members
            |> List.tryFind (fun (memberName, _) -> memberName = name)
            |> Option.map snd

        let fallback () =
            JObject(obj.Members |> List.map (fun (name, value) -> name, this.ConvertValue value))

        match tryMember "Keys", tryMember "Values" with
        | Some keys, Some values ->
            match this.TryGetArrayItems keys, this.TryGetArrayItems values with
            | Some keyItems, Some valueItems ->
                let keyItems = List.toArray keyItems
                let valueItems = List.toArray valueItems
                let count = min keyItems.Length valueItems.Length
                JObject [
                    for i in 0 .. count - 1 ->
                        let key = this.JsonKey(this.ConvertValue keyItems.[i])
                        key, this.ConvertValue valueItems.[i]
                ]
            | _ -> fallback ()
        | _ -> fallback ()

    member private this.ConvertList(obj: SerializedObject) : JsonValue =
        let tryMember name =
            obj.Members
            |> List.tryFind (fun (memberName, _) -> memberName = name)
            |> Option.map snd

        let items =
            match tryMember "_items" with
            | Some value -> this.TryGetArrayItems(value) |> Option.defaultValue []
            | None -> []

        let size =
            match tryMember "_size" with
            | Some value ->
                match this.Resolve(value) with
                | PrimVal(PInt32 n) -> n
                | _ -> List.length items
            | None -> List.length items

        let items = List.toArray items
        let count = max 0 (min size items.Length)
        JArray [ for i in 0 .. count - 1 -> this.ConvertValue items.[i] ]

    member private this.ConvertArray(obj: SerializedObject) : JsonValue =
        let items = obj.Items |> List.map this.ConvertValue
        if obj.Lengths.Length <= 1 then
            JArray items
        else
            nestArray obj.Lengths items

    member private this.ConvertClass(obj: SerializedObject) : JsonValue =
        let className = obj.ClassName

        if className = "System.Collections.Hashtable" then
            this.ConvertHashtable(obj)
        elif className.StartsWith("System.Collections.Generic.List`1", StringComparison.Ordinal) then
            this.ConvertList(obj)
        elif obj.Members.Length = 1 && fst obj.Members.Head = "value__" then
            // A boxed enum is serialized as a class with a single value__ field.
            this.ConvertValue(snd obj.Members.Head)
        else
            JObject(obj.Members |> List.map (fun (name, value) -> name, this.ConvertValue value))

    member private this.ConvertObject(obj: SerializedObject) : JsonValue =
        if resolving.Contains(obj.Id) then
            // Keep the output finite for cyclic object graphs.
            JObject [ "$ref", JNumber(obj.Id.ToString(CultureInfo.InvariantCulture)) ]
        else
            resolving.Add(obj.Id) |> ignore
            try
                match obj.Kind with
                | KString -> JString obj.Text
                | KArray -> this.ConvertArray(obj)
                | KObject -> this.ConvertClass(obj)
            finally
                resolving.Remove(obj.Id) |> ignore

    member this.ConvertValue(value: SerializedValue) : JsonValue =
        match this.Resolve(value) with
        | NullVal -> JNull
        | PrimVal primitive -> primitiveToJson primitive
        | ObjVal obj -> this.ConvertObject(obj)
        | RefVal _ -> JNull

    member this.ConvertRoot(rootId: int) : JsonValue =
        match objects.TryGetValue(rootId) with
        | true, obj -> this.ConvertObject(obj)
        | _ -> failwithf "Root object with id %d was not found" rootId


// ---------------------------------------------------------------------------
// Writer (serializes the object graph back to MS-NRBF)
// ---------------------------------------------------------------------------

let private primitiveTypeOfValue (value: PrimitiveValue) : PrimitiveType =
    match value with
    | PBool _ -> PrimitiveType.Boolean
    | PByte _ -> PrimitiveType.Byte
    | PChar _ -> PrimitiveType.Char
    | PDecimal _ -> PrimitiveType.Decimal
    | PDouble _ -> PrimitiveType.Double
    | PInt16 _ -> PrimitiveType.Int16
    | PInt32 _ -> PrimitiveType.Int32
    | PInt64 _ -> PrimitiveType.Int64
    | PSByte _ -> PrimitiveType.SByte
    | PSingle _ -> PrimitiveType.Single
    | PTimeSpan _ -> PrimitiveType.TimeSpan
    | PDateTime _ -> PrimitiveType.DateTime
    | PUInt16 _ -> PrimitiveType.UInt16
    | PUInt32 _ -> PrimitiveType.UInt32
    | PUInt64 _ -> PrimitiveType.UInt64
    | PNull -> PrimitiveType.Null
    | PString _ -> PrimitiveType.String

type NrbfWriter
    (stream: Stream, objects: Dictionary<int, SerializedObject>, libraries: (int * string) list) =
    let writer = new BinaryWriter(stream, Encoding.UTF8, true)
    let emitted = HashSet<int>()

    member private _.Write7BitEncodedInt(value: int) =
        let mutable remaining = value
        let mutable more = true
        while more do
            let b = byte (remaining &&& 0x7F)
            remaining <- remaining >>> 7
            if remaining <> 0 then
                writer.Write(b ||| 0x80uy)
            else
                writer.Write(b)
                more <- false

    member private this.WriteString(value: string) =
        let bytes = Encoding.UTF8.GetBytes(value)
        this.Write7BitEncodedInt(bytes.Length)
        writer.Write(bytes)

    member private _.WriteTypeCode(binaryType: BinaryType) =
        match binaryType with
        | PrimitiveTypeInfo _ -> writer.Write(0uy)
        | StringType -> writer.Write(1uy)
        | ObjectType -> writer.Write(2uy)
        | SystemClassType _ -> writer.Write(3uy)
        | ClassType _ -> writer.Write(4uy)
        | ObjectArrayType -> writer.Write(5uy)
        | StringArrayType -> writer.Write(6uy)
        | PrimitiveArrayType _ -> writer.Write(7uy)

    member private this.WriteTypeAdditionalInfo(binaryType: BinaryType) =
        match binaryType with
        | PrimitiveTypeInfo primitive -> writer.Write(byte (int primitive))
        | SystemClassType className -> this.WriteString(className)
        | ClassType(className, libraryId) ->
            this.WriteString(className)
            writer.Write(libraryId)
        | PrimitiveArrayType primitive -> writer.Write(byte (int primitive))
        | StringType
        | ObjectType
        | ObjectArrayType
        | StringArrayType -> ()

    member private this.WriteTypeInfo(binaryType: BinaryType) =
        this.WriteTypeCode(binaryType)
        this.WriteTypeAdditionalInfo(binaryType)

    member private this.WritePrimitiveValue(primitive: PrimitiveType, value: PrimitiveValue) =
        match primitive, value with
        | PrimitiveType.Boolean, PBool b -> writer.Write(b)
        | PrimitiveType.Byte, PByte b -> writer.Write(b)
        | PrimitiveType.Char, PChar c -> writer.Write(c)
        | PrimitiveType.Decimal, PDecimal d -> writer.Write(d)
        | PrimitiveType.Double, PDouble d -> writer.Write(d)
        | PrimitiveType.Int16, PInt16 n -> writer.Write(n)
        | PrimitiveType.Int32, PInt32 n -> writer.Write(n)
        | PrimitiveType.Int64, PInt64 n -> writer.Write(n)
        | PrimitiveType.SByte, PSByte n -> writer.Write(n)
        | PrimitiveType.Single, PSingle n -> writer.Write(n)
        | PrimitiveType.TimeSpan, PTimeSpan ticks -> writer.Write(ticks)
        | PrimitiveType.DateTime, PDateTime raw -> writer.Write(raw)
        | PrimitiveType.UInt16, PUInt16 n -> writer.Write(n)
        | PrimitiveType.UInt32, PUInt32 n -> writer.Write(n)
        | PrimitiveType.UInt64, PUInt64 n -> writer.Write(n)
        | PrimitiveType.Null, PNull -> ()
        | PrimitiveType.String, PString s -> this.WriteString(s)
        | _ ->
            failwithf "Value %A does not match the declared primitive type %A" value primitive

    /// Writes a value that appears inside a class member or an array.  Object
    /// definitions are always emitted as separate top-level records, so this
    /// only emits nulls, boxed primitives and object references.
    member private this.WriteReference(value: SerializedValue) =
        match value with
        | NullVal -> writer.Write(0x0Auy)
        | PrimVal primitive ->
            writer.Write(0x08uy) // MemberPrimitiveTyped
            writer.Write(byte (int (primitiveTypeOfValue primitive)))
            this.WritePrimitiveValue(primitiveTypeOfValue primitive, primitive)
        | RefVal id ->
            if not (objects.ContainsKey(id)) then
                failwithf "Cannot write reference to unknown object id %d" id
            writer.Write(0x09uy) // MemberReference
            writer.Write(id)
        | ObjVal obj ->
            writer.Write(0x09uy) // MemberReference
            writer.Write(obj.Id)

    member private this.WriteClass(obj: SerializedObject) =
        let members = List.toArray obj.Members
        if obj.MemberTypes.Length <> members.Length then
            failwithf
                "Class %s has %d members but %d member types"
                obj.ClassName
                members.Length
                obj.MemberTypes.Length

        let recordType =
            match obj.LibraryId with
            | Some _ -> 0x05uy // ClassWithMembersAndTypes
            | None -> 0x04uy // SystemClassWithMembersAndTypes

        writer.Write(recordType)
        writer.Write(obj.Id)
        this.WriteString(obj.ClassName)
        writer.Write(members.Length)
        for memberName, _ in members do
            this.WriteString(memberName)
        for binaryType in obj.MemberTypes do
            this.WriteTypeCode(binaryType)
        for binaryType in obj.MemberTypes do
            this.WriteTypeAdditionalInfo(binaryType)
        match obj.LibraryId with
        | Some libraryId -> writer.Write(libraryId)
        | None -> ()

        for i in 0 .. members.Length - 1 do
            let _, value = members.[i]
            match obj.MemberTypes.[i] with
            | PrimitiveTypeInfo primitive ->
                match value with
                | PrimVal p -> this.WritePrimitiveValue(primitive, p)
                | _ ->
                    failwithf
                        "Member %s of class %s should be a primitive value"
                        (fst members.[i])
                        obj.ClassName
            | _ -> this.WriteReference(value)

    member private this.WriteArray(obj: SerializedObject) =
        let lengths =
            if obj.Lengths.Length = 0 then [| obj.Items.Length |] else obj.Lengths
        let arrayType = defaultArg obj.ArrayType 0uy
        let elementType = defaultArg obj.ArrayElementType ObjectType

        // BinaryFormatter uses the compact single-dimensional records when it
        // can, and only falls back to BinaryArray for multi-dimensional arrays.
        if lengths.Length = 1 && arrayType = 0uy then
            match elementType with
            | PrimitiveTypeInfo primitive ->
                writer.Write(0x0Fuy) // ArraySinglePrimitive
                writer.Write(obj.Id)
                writer.Write(lengths.[0])
                writer.Write(byte (int primitive))
                for item in obj.Items do
                    match item with
                    | PrimVal p -> this.WritePrimitiveValue(primitive, p)
                    | _ -> failwithf "Primitive array %d contains a non-primitive element" obj.Id
            | StringType ->
                writer.Write(0x11uy) // ArraySingleString
                writer.Write(obj.Id)
                writer.Write(lengths.[0])
                for item in obj.Items do
                    this.WriteReference(item)
            | ObjectType ->
                writer.Write(0x10uy) // ArraySingleObject
                writer.Write(obj.Id)
                writer.Write(lengths.[0])
                for item in obj.Items do
                    this.WriteReference(item)
            | _ ->
                this.WriteBinaryArray(obj, lengths, arrayType, elementType)
        else
            this.WriteBinaryArray(obj, lengths, arrayType, elementType)

    member private this.WriteBinaryArray
        (obj: SerializedObject, lengths: int[], arrayType: byte, elementType: BinaryType)
        =
        writer.Write(0x07uy) // BinaryArray
        writer.Write(obj.Id)
        writer.Write(arrayType)
        writer.Write(lengths.Length)
        for length in lengths do
            writer.Write(length)

        if arrayType = 3uy || arrayType = 4uy || arrayType = 5uy then
            match obj.LowerBounds with
            | Some lowerBounds when lowerBounds.Length = lengths.Length ->
                for lowerBound in lowerBounds do
                    writer.Write(lowerBound)
            | _ ->
                for _ in 1 .. lengths.Length do
                    writer.Write(0)

        this.WriteTypeInfo(elementType)

        match elementType with
        | PrimitiveTypeInfo primitive
        | PrimitiveArrayType primitive ->
            for item in obj.Items do
                match item with
                | PrimVal p -> this.WritePrimitiveValue(primitive, p)
                | _ -> failwithf "Array object %d contains a non-primitive element" obj.Id
        | _ ->
            for item in obj.Items do
                this.WriteReference(item)

    member private this.WriteObjectDefinition(obj: SerializedObject) =
        if emitted.Add(obj.Id) then
            match obj.Kind with
            | KString ->
                writer.Write(0x06uy) // BinaryObjectString
                writer.Write(obj.Id)
                this.WriteString(obj.Text)
            | KArray -> this.WriteArray(obj)
            | KObject -> this.WriteClass(obj)

    member this.WriteHeader(rootId: int) =
        writer.Write(0x00uy) // SerializationHeaderRecord
        writer.Write(rootId)
        writer.Write(-1) // header id
        writer.Write(1) // major version
        writer.Write(0) // minor version

        for libraryId, name in libraries |> List.sortBy fst do
            writer.Write(0x0Cuy) // BinaryLibrary
            writer.Write(libraryId)
            this.WriteString(name)

    member this.WriteRoot(root: SerializedObject) =
        // Emit each reachable object once as a top-level record.  A post-order
        // walk keeps most references backward; cycles are handled by the
        // seen-set and simply become forward references.
        let seen = HashSet<int>()
        let ordered = ResizeArray<SerializedObject>()

        let rec visit (value: SerializedValue) =
            match value with
            | NullVal
            | PrimVal _ -> ()
            | RefVal id ->
                match objects.TryGetValue(id) with
                | true, obj -> visit (ObjVal obj)
                | _ -> failwithf "Cannot write reference to unknown object id %d" id
            | ObjVal obj ->
                if seen.Add(obj.Id) then
                    match obj.Kind with
                    | KObject ->
                        for _, memberValue in obj.Members do
                            visit memberValue
                    | KArray ->
                        for item in obj.Items do
                            visit item
                    | KString -> ()
                    ordered.Add(obj)

        visit (ObjVal root)
        for obj in ordered do
            this.WriteObjectDefinition(obj)

    member this.WriteMessageEnd() =
        writer.Write(0x0Buy)
        writer.Flush()

// ---------------------------------------------------------------------------
// JSON parsing and merging edits back into the object graph
// ---------------------------------------------------------------------------

module JsonParser =
    open System.Text.Json

    let rec private ofElement (element: JsonElement) : JsonValue =
        match element.ValueKind with
        | JsonValueKind.Null -> JNull
        | JsonValueKind.True -> JBool true
        | JsonValueKind.False -> JBool false
        | JsonValueKind.Number -> JNumber(element.GetRawText())
        | JsonValueKind.String -> JString(element.GetString())
        | JsonValueKind.Array ->
            JArray [ for item in element.EnumerateArray() -> ofElement item ]
        | JsonValueKind.Object ->
            JObject [ for property in element.EnumerateObject() -> property.Name, ofElement property.Value ]
        | other -> failwithf "Unsupported JSON value kind %A" other

    let parse (text: string) : JsonValue =
        use document = JsonDocument.Parse(text)
        ofElement document.RootElement

let resolveValue (objects: Dictionary<int, SerializedObject>) (value: SerializedValue) =
    match value with
    | RefVal id ->
        match objects.TryGetValue(id) with
        | true, obj -> ObjVal obj
        | _ -> NullVal
    | other -> other

module JsonMerge =
    let private invariant = CultureInfo.InvariantCulture

    let private expect message =
        failwithf "Invalid edit: %s" message

    let private mergePrimitive (original: PrimitiveValue) (edited: JsonValue) : PrimitiveValue =
        match original, edited with
        | PNull, JNull -> PNull
        | PBool _, JBool b -> PBool b
        | PByte _, JNumber n -> PByte(Byte.Parse(n, NumberStyles.Integer, invariant))
        | PChar _, JString s when s.Length = 1 -> PChar s.[0]
        | PDecimal _, JNumber n -> PDecimal(Decimal.Parse(n, NumberStyles.Number, invariant))
        | PDouble _, JNumber n -> PDouble(Double.Parse(n, NumberStyles.Float, invariant))
        | PInt16 _, JNumber n -> PInt16(Int16.Parse(n, NumberStyles.Integer, invariant))
        | PInt32 _, JNumber n -> PInt32(Int32.Parse(n, NumberStyles.Integer, invariant))
        | PInt64 _, JNumber n -> PInt64(Int64.Parse(n, NumberStyles.Integer, invariant))
        | PSByte _, JNumber n -> PSByte(SByte.Parse(n, NumberStyles.Integer, invariant))
        | PSingle _, JNumber n -> PSingle(Single.Parse(n, NumberStyles.Float, invariant))
        | PTimeSpan _, JString s -> PTimeSpan(TimeSpan.Parse(s, invariant).Ticks)
        | PDateTime _, JString s ->
            PDateTime(DateTime.Parse(s, invariant, DateTimeStyles.RoundtripKind).ToBinary())
        | PUInt16 _, JNumber n -> PUInt16(UInt16.Parse(n, NumberStyles.Integer, invariant))
        | PUInt32 _, JNumber n -> PUInt32(UInt32.Parse(n, NumberStyles.Integer, invariant))
        | PUInt64 _, JNumber n -> PUInt64(UInt64.Parse(n, NumberStyles.Integer, invariant))
        | PString _, JString s -> PString s
        | _ ->
            expect (
                sprintf
                    "expected a value compatible with %A, got %s"
                    original
                    (JsonWriter.serialize false edited)
            )

    let private isEnumObject (obj: SerializedObject) =
        obj.Kind = KObject
        && obj.Members.Length = 1
        && fst obj.Members.Head = "value__"

    let private memberValue (obj: SerializedObject) (name: string) =
        obj.Members
        |> List.tryFind (fun (memberName, _) -> memberName = name)
        |> Option.map snd

    let private arrayItems (objects: Dictionary<int, SerializedObject>) (value: SerializedValue) =
        match resolveValue objects value with
        | ObjVal obj when obj.Kind = KArray -> Some obj.Items
        | _ -> None

    let rec private mergeObject
        (objects: Dictionary<int, SerializedObject>)
        (obj: SerializedObject)
        (edited: JsonValue)
        =
        match obj.Kind with
        | KString ->
            match edited with
            | JString text -> obj.Text <- text
            | _ -> expect "a string value was expected"
        | KArray ->
            match edited with
            | JArray items ->
                let original = List.toArray obj.Items
                if items.Length <> original.Length then
                    expect (
                        sprintf
                            "this array must keep %d elements (got %d)"
                            original.Length
                            items.Length
                    )
                obj.Items <-
                    [ for i in 0 .. original.Length - 1 -> mergeValue objects original.[i] items.[i] ]
            | _ -> expect "an array was expected"
        | KObject ->
            if isEnumObject obj then
                let value = snd obj.Members.Head
                let editedValue =
                    match edited with
                    | JObject properties ->
                        match properties |> List.tryFind (fun (name, _) -> name = "value__") with
                        | Some(_, value) -> value
                        | None -> edited
                    | other -> other
                obj.Members <- [ fst obj.Members.Head, mergeValue objects value editedValue ]
            elif obj.ClassName = "System.Collections.Hashtable" then
                mergeHashtable objects obj edited
            elif obj.ClassName.StartsWith("System.Collections.Generic.List`1", StringComparison.Ordinal) then
                mergeList objects obj edited
            else
                mergeClass objects obj edited

    and private mergeMemberValue
        (objects: Dictionary<int, SerializedObject>)
        (declaredType: BinaryType)
        (original: SerializedValue)
        (edited: JsonValue)
        =
        match declaredType with
        | PrimitiveTypeInfo _ ->
            match original with
            | PrimVal primitive -> PrimVal(mergePrimitive primitive edited)
            | _ -> expect "a primitive member was expected"
        | _ -> mergeValue objects original edited

    and private mergeClass
        (objects: Dictionary<int, SerializedObject>)
        (obj: SerializedObject)
        (edited: JsonValue)
        =
        match edited with
        | JObject properties ->
            let knownMembers = obj.Members |> List.map fst |> Set.ofList
            for name, _ in properties do
                if not (knownMembers.Contains name) then
                    expect (sprintf "unknown field %s on %s" name obj.ClassName)

            let types = obj.MemberTypes
            obj.Members <-
                obj.Members
                |> List.mapi (fun index (name, value) ->
                    match properties |> List.tryFind (fun (propertyName, _) -> propertyName = name) with
                    | None -> name, value
                    | Some(_, editedValue) ->
                        let declaredType =
                            if index < types.Length then types.[index] else ObjectType
                        name, mergeMemberValue objects declaredType value editedValue)
        | _ -> expect (sprintf "an object was expected for %s" obj.ClassName)

    and private mergeList
        (objects: Dictionary<int, SerializedObject>)
        (obj: SerializedObject)
        (edited: JsonValue)
        =
        match edited with
        | JArray items ->
            let size =
                match memberValue obj "_size" with
                | Some value ->
                    match resolveValue objects value with
                    | PrimVal(PInt32 n) -> n
                    | _ -> -1
                | None -> -1

            if size < 0 then
                expect "the list does not have a valid _size field"
            if items.Length <> size then
                expect (
                    sprintf
                        "this list must keep %d elements (got %d)"
                        size
                        items.Length
                )

            match memberValue obj "_items" with
            | Some itemsValue ->
                match resolveValue objects itemsValue with
                | ObjVal arrayObject when arrayObject.Kind = KArray ->
                    let original = List.toArray arrayObject.Items
                    if items.Length > original.Length then
                        expect "the list backing array is too small"
                    arrayObject.Items <-
                        [ for i in 0 .. original.Length - 1 ->
                            if i < items.Length then
                                mergeValue objects original.[i] items.[i]
                            else
                                original.[i] ]
                | NullVal when items.Length = 0 -> ()
                | _ -> expect "the list backing array could not be found"
            | None -> expect "the list does not have an _items field"
        | _ -> expect "an array was expected for a List<T>"

    and private mergeHashtable
        (objects: Dictionary<int, SerializedObject>)
        (obj: SerializedObject)
        (edited: JsonValue)
        =
        match edited with
        | JObject properties ->
            let keys =
                match memberValue obj "Keys" with
                | Some value -> arrayItems objects value
                | None -> None
            let values =
                match memberValue obj "Values" with
                | Some value -> arrayItems objects value
                | None -> None

            match keys, values with
            | Some keyItems, Some valueItems ->
                let keyItems = List.toArray keyItems
                let valueItems = List.toArray valueItems
                let keyTexts =
                    keyItems
                    |> Array.map (fun key ->
                        match resolveValue objects key with
                        | ObjVal keyObject when keyObject.Kind = KString -> keyObject.Text
                        | _ -> JsonWriter.serialize false (JsonConverter(objects).ConvertValue key))

                let indices = Dictionary<string, int>()
                for i in 0 .. keyTexts.Length - 1 do
                    indices.[keyTexts.[i]] <- i

                for name, editedValue in properties do
                    match indices.TryGetValue(name) with
                    | true, index when index < valueItems.Length ->
                        valueItems.[index] <- mergeValue objects valueItems.[index] editedValue
                    | _ ->
                        expect (
                            sprintf
                                "adding or renaming Hashtable key %s is not supported"
                                name
                        )

                match memberValue obj "Values" with
                | Some valuesValue ->
                    match resolveValue objects valuesValue with
                    | ObjVal valuesObject when valuesObject.Kind = KArray ->
                        valuesObject.Items <- List.ofArray valueItems
                    | _ -> expect "the Hashtable Values array could not be found"
                | None -> expect "the Hashtable does not have a Values field"
            | _ -> expect "the Hashtable Keys/Values arrays could not be found"
        | _ -> expect "an object was expected for a Hashtable"

    and mergeValue
        (objects: Dictionary<int, SerializedObject>)
        (original: SerializedValue)
        (edited: JsonValue)
        : SerializedValue =
        match resolveValue objects original, edited with
        | NullVal, JNull -> NullVal
        | NullVal, _ -> expect "cannot create a value where the original was null"
        | PrimVal primitive, _ -> PrimVal(mergePrimitive primitive edited)
        | ObjVal _, JNull -> NullVal
        | ObjVal obj, _ ->
            mergeObject objects obj edited
            ObjVal obj
        | RefVal _, _ -> failwith "unreachable: references are resolved before merging"

// ---------------------------------------------------------------------------
// A save document: the parsed graph plus helpers for the GUI and CLI
// ---------------------------------------------------------------------------

type SaveDocument =
    { RootId: int
      Root: SerializedObject
      Objects: Dictionary<int, SerializedObject>
      Libraries: (int * string) list
      SourcePath: string option }

module SaveDocument =
    let load (bytes: byte[]) : SaveDocument =
        use stream = new MemoryStream(bytes)
        let reader = NrbfReader(stream)
        reader.Parse()

        let header =
            match reader.Header with
            | Some value -> value
            | None -> failwith "The file does not contain a SerializationHeaderRecord"

        let root =
            match reader.Objects.TryGetValue(header.RootId) with
            | true, obj -> obj
            | _ -> failwithf "Root object with id %d was not found" header.RootId

        { RootId = header.RootId
          Root = root
          Objects = reader.Objects
          Libraries = reader.Libraries |> Seq.map (fun pair -> pair.Key, pair.Value) |> Seq.toList
          SourcePath = None }

    let loadFromFile (path: string) : SaveDocument =
        let document = load (File.ReadAllBytes(path))
        { document with SourcePath = Some path }

    let toJson (document: SaveDocument) : JsonValue =
        JsonConverter(document.Objects).ConvertRoot(document.RootId)

    let save (document: SaveDocument) : byte[] =
        use stream = new MemoryStream()
        let writer = NrbfWriter(stream, document.Objects, document.Libraries)
        writer.WriteHeader(document.RootId)
        writer.WriteRoot(document.Root)
        writer.WriteMessageEnd()
        stream.ToArray()

    let private memberValue (obj: SerializedObject) name =
        obj.Members
        |> List.tryFind (fun (memberName, _) -> memberName = name)
        |> Option.map snd

    let private arrayItems (objects: Dictionary<int, SerializedObject>) value =
        match resolveValue objects value with
        | ObjVal obj when obj.Kind = KArray -> Some obj.Items
        | _ -> None

    let private keyText (objects: Dictionary<int, SerializedObject>) value =
        match resolveValue objects value with
        | ObjVal obj when obj.Kind = KString -> obj.Text
        | other -> JsonWriter.serialize false (JsonConverter(objects).ConvertValue other)

    /// The root Hashtable represented as human-readable key/value pairs.
    let topLevelEntries (document: SaveDocument) : (string * SerializedValue) list =
        if document.Root.Kind = KObject && document.Root.ClassName = "System.Collections.Hashtable" then
            match memberValue document.Root "Keys", memberValue document.Root "Values" with
            | Some keys, Some values ->
                match arrayItems document.Objects keys, arrayItems document.Objects values with
                | Some keyItems, Some valueItems ->
                    List.zip keyItems valueItems
                    |> List.map (fun (key, value) -> keyText document.Objects key, value)
                | _ -> [ "$root", ObjVal document.Root ]
            | _ -> [ "$root", ObjVal document.Root ]
        else
            [ "$root", ObjVal document.Root ]

    /// Apply an edited JSON value to one entry of the root Hashtable.  The saved
    /// file is produced from the modified graph, never by patching raw bytes.
    let applyTopLevelEdit (document: SaveDocument) (key: string) (edited: JsonValue) =
        if document.Root.Kind <> KObject || document.Root.ClassName <> "System.Collections.Hashtable" then
            failwith "The root object is not a Hashtable"

        let keys =
            match memberValue document.Root "Keys" with
            | Some value -> arrayItems document.Objects value
            | None -> None
        let values =
            match memberValue document.Root "Values" with
            | Some value -> arrayItems document.Objects value
            | None -> None

        match keys, values with
        | Some keyItems, Some valueItems ->
            let keyItems = List.toArray keyItems
            let valueItems = List.toArray valueItems
            let index =
                keyItems
                |> Array.tryFindIndex (fun keyValue -> keyText document.Objects keyValue = key)

            match index with
            | Some i when i < valueItems.Length ->
                valueItems.[i] <- JsonMerge.mergeValue document.Objects valueItems.[i] edited
                match memberValue document.Root "Values" with
                | Some valuesValue ->
                    match resolveValue document.Objects valuesValue with
                    | ObjVal valuesObject when valuesObject.Kind = KArray ->
                        valuesObject.Items <- List.ofArray valueItems
                    | _ -> failwith "The root Hashtable Values array could not be found"
                | None -> failwith "The root Hashtable has no Values field"
            | _ -> failwithf "Save key %s was not found" key
        | _ -> failwith "The root Hashtable Keys/Values arrays could not be found"
